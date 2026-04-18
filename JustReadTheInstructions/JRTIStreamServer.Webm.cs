using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace JustReadTheInstructions
{
    public partial class JRTIStreamServer
    {
        private static void FixWebm(string path)
        {
            try { FixWebmInternal(path); }
            catch (Exception ex) { Debug.LogError($"[JRTI-Stream]: FixWebm crash:\n{ex}"); }
        }

        private static void FixWebmInternal(string path)
        {
            byte[] d;
            try
            {
                var fi = new FileInfo(path);
                if (fi.Length > 2L * 1024 * 1024 * 1024) { Debug.LogWarning($"[JRTI-Stream]: FixWebm skip (>2GB): {path}"); return; }
                d = File.ReadAllBytes(path);
            }
            catch (Exception ex) { Debug.LogWarning($"[JRTI-Stream]: FixWebm read failed: {ex.Message}"); return; }

            int i = 0;

            if (!WbReadId(d, ref i, out uint ebmlId) || ebmlId != 0x1A45DFA3)
            { Debug.LogWarning("[JRTI-Stream]: FixWebm: not an EBML file"); return; }
            if (!WbReadSize(d, ref i, out long ebmlBodySize))
            { Debug.LogWarning("[JRTI-Stream]: FixWebm: bad EBML size"); return; }
            i += (int)Math.Min(ebmlBodySize == WbUnknownSize ? 0 : ebmlBodySize, Math.Max(0, d.Length - i));

            if (!WbReadId(d, ref i, out uint segId) || segId != 0x18538067)
            { Debug.LogWarning("[JRTI-Stream]: FixWebm: no Segment element"); return; }
            if (!WbReadSize(d, ref i, out long segBodySize)) return;

            int segDataStart = i;
            int segDataEnd = (segBodySize == WbUnknownSize || (long)segDataStart + segBodySize > d.Length)
                ? d.Length
                : segDataStart + (int)segBodySize;

            int segInfoStart = -1, segInfoEnd = -1;
            long timecodeScale = 1000000;
            bool hasDuration = false;
            var clusters = new List<(int absStart, long time)>();

            int j = segDataStart;
            while (j < segDataEnd && j < d.Length)
            {
                int elStart = j;
                if (!WbReadId(d, ref j, out uint elId)) break;
                if (!WbReadSize(d, ref j, out long elSize)) break;
                int dataOff = j;

                int elEnd;
                if (elSize == WbUnknownSize || (long)dataOff + elSize > segDataEnd)
                    elEnd = segDataEnd;
                else
                    elEnd = dataOff + (int)elSize;

                if (elId == 0x1549A966)
                {
                    segInfoStart = elStart;
                    segInfoEnd = elEnd;
                    int k = dataOff;
                    while (k < elEnd && k < d.Length)
                    {
                        if (!WbReadId(d, ref k, out uint subId)) break;
                        if (!WbReadSize(d, ref k, out long subSize)) break;
                        int subDataOff = k;
                        int subEnd = (subSize == WbUnknownSize || k + subSize > elEnd) ? elEnd : k + (int)subSize;
                        if (subId == 0x2AD7B1 && subSize <= 8 && subSize > 0)
                        {
                            long ts = 0;
                            for (int x = 0; x < (int)subSize; x++) ts = ts * 256 + d[k + x];
                            timecodeScale = ts == 0 ? 1000000 : ts;
                        }
                        else if (subId == 0x4489)
                            hasDuration = true;
                        k = subEnd;
                    }
                }
                else if (elId == 0x1F43B675)
                {
                    long clusterTime = 0;
                    int k = dataOff;
                    while (k < elEnd && k < d.Length)
                    {
                        if (!WbReadId(d, ref k, out uint subId)) break;
                        if (!WbReadSize(d, ref k, out long subSize)) break;
                        int subEnd = (subSize == WbUnknownSize || k + subSize > elEnd) ? elEnd : k + (int)subSize;
                        if (subId == 0xE7 && subSize <= 8 && subSize > 0)
                        {
                            long t = 0;
                            for (int x = 0; x < (int)subSize; x++) t = t * 256 + d[k + x];
                            clusterTime = t;
                            break;
                        }
                        k = subEnd;
                    }
                    clusters.Add((elStart, clusterTime));
                }

                j = elEnd;
            }

            if (segInfoStart < 0 || clusters.Count == 0)
            {
                Debug.LogWarning($"[JRTI-Stream]: FixWebm abort — no SegInfo or clusters. path={path}");
                return;
            }

            if (hasDuration)
            {
                Debug.Log($"[JRTI-Stream]: FixWebm: Duration already present in {path}");
                return;
            }

            long lastClusterTime = clusters[clusters.Count - 1].time;
            long durationTicks = lastClusterTime + 33;

            byte[] durationEl = WbBuildFloat64(0x4489, durationTicks);

            int infoIdLen = WbIdLen(d, segInfoStart);
            int infoSzLen = WbSizeLen(d, segInfoStart + infoIdLen);
            byte[] oldInfoPayload = Slice(d, segInfoStart + infoIdLen + infoSzLen, segInfoEnd);
            byte[] newInfoEl = WbBuildContainerRaw(0x1549A966, Concat(oldInfoPayload, durationEl));

            int infoSizeDelta = newInfoEl.Length - (segInfoEnd - segInfoStart);
            byte[] cuesElPass1 = WbBuildCues(clusters, segDataStart, 0);
            long clusterShift = infoSizeDelta + cuesElPass1.Length;
            byte[] cuesEl = WbBuildCues(clusters, segDataStart, clusterShift);
            int firstClusterAbsPos = clusters[0].absStart;

            byte[] final = Concat(
                Slice(d, 0, segInfoStart),
                newInfoEl,
                Slice(d, segInfoEnd, firstClusterAbsPos),
                cuesEl,
                Slice(d, firstClusterAbsPos, d.Length)
            );

            try
            {
                File.WriteAllBytes(path, final);
                Debug.Log($"[JRTI-Stream]: FixWebm OK — {clusters.Count} clusters, Duration={durationTicks} ticks injected: {path}");
            }
            catch (Exception ex) { Debug.LogError($"[JRTI-Stream]: FixWebm write failed: {ex.Message}"); }
        }

        private static readonly long WbUnknownSize = unchecked((long)0x00FFFFFFFFFFFFFFL);

        private static byte[] WbBuildCues(List<(int absStart, long time)> clusters, int segDataStart, long shift = 0)
        {
            var points = new List<byte[]>();
            foreach (var (absStart, time) in clusters)
            {
                long clusterPos = Math.Max(0, absStart - segDataStart + shift);
                byte[] trackPos = WbBuildContainerRaw(0xB7, Concat(
                    WbBuildUint(0xF7, 1),
                    WbBuildUint(0xF1, clusterPos)
                ));
                byte[] cuePoint = WbBuildContainerRaw(0xBB, Concat(
                    WbBuildUint(0xB3, time),
                    trackPos
                ));
                points.Add(cuePoint);
            }
            return WbBuildContainerRaw(0x1C53BB6B, Concat(points.ToArray()));
        }

        private static byte[] WbBuildFloat64(uint id, double val)
        {
            byte[] idBytes = WbEncodeId(id);
            byte[] payload = new byte[8];
            long bits = BitConverter.DoubleToInt64Bits(val);
            for (int i = 7; i >= 0; i--) { payload[i] = (byte)(bits & 0xFF); bits >>= 8; }
            byte[] sizeBytes = WbEncodeVint(8, 1);
            return Concat(idBytes, sizeBytes, payload);
        }

        private static byte[] WbBuildUint(uint id, long val)
        {
            if (val < 0) val = 0;
            int byteLen = 1;
            long tmp = val;
            while (tmp > 0xFF) { byteLen++; tmp >>= 8; }
            byte[] payload = new byte[byteLen];
            long v = val;
            for (int i = byteLen - 1; i >= 0; i--) { payload[i] = (byte)(v & 0xFF); v >>= 8; }
            return Concat(WbEncodeId(id), WbEncodeVint(byteLen, 1), payload);
        }

        private static byte[] WbBuildContainerRaw(uint id, byte[] payload)
        {
            return Concat(WbEncodeId(id), WbEncodeVint(payload.Length, WbVintWidth(payload.Length)), payload);
        }

        private static byte[] WbEncodeId(uint id)
        {
            if (id <= 0xFF) return new byte[] { (byte)id };
            if (id <= 0xFFFF) return new byte[] { (byte)(id >> 8), (byte)id };
            if (id <= 0xFFFFFF) return new byte[] { (byte)(id >> 16), (byte)(id >> 8), (byte)id };
            return new byte[] { (byte)(id >> 24), (byte)(id >> 16), (byte)(id >> 8), (byte)id };
        }

        private static byte[] WbEncodeVint(long val, int width)
        {
            var b = new byte[width];
            int marker = 0x80 >> (width - 1);
            long v = val;
            for (int i = width - 1; i > 0; i--) { b[i] = (byte)(v & 0xFF); v >>= 8; }
            b[0] = (byte)(((byte)(v & 0x7F) & (byte)(marker - 1)) | (byte)marker);
            return b;
        }

        private static int WbVintWidth(long val)
        {
            if (val < 0x7F) return 1;
            if (val < 0x3FFF) return 2;
            if (val < 0x1FFFFF) return 3;
            if (val < 0x0FFFFFFF) return 4;
            return 8;
        }

        private static bool WbReadId(byte[] d, ref int i, out uint id)
        {
            id = 0;
            if (i >= d.Length) return false;
            byte b = d[i];
            if (b == 0) return false;
            int width = 1;
            byte mask = 0x80;
            while ((b & mask) == 0 && width <= 4) { width++; mask >>= 1; }
            if (i + width > d.Length) return false;
            uint val = b;
            for (int x = 1; x < width; x++) val = (val << 8) | d[i + x];
            id = val;
            i += width;
            return true;
        }

        private static bool WbReadSize(byte[] d, ref int i, out long size)
        {
            size = 0;
            if (i >= d.Length) return false;
            byte b = d[i];
            if (b == 0) return false;
            int width = 1;
            byte mask = 0x80;
            while ((b & mask) == 0 && width <= 8) { width++; mask >>= 1; }
            if (i + width > d.Length) return false;
            long val = b & (mask - 1);
            for (int x = 1; x < width; x++) val = (val << 8) | d[i + x];
            i += width;
            size = val;
            return true;
        }

        private static int WbIdLen(byte[] d, int offset)
        {
            if (offset >= d.Length) return 1;
            byte b = d[offset];
            int width = 1;
            byte mask = 0x80;
            while ((b & mask) == 0 && width <= 4) { width++; mask >>= 1; }
            return width;
        }

        private static int WbSizeLen(byte[] d, int offset)
        {
            if (offset >= d.Length) return 1;
            byte b = d[offset];
            int width = 1;
            byte mask = 0x80;
            while ((b & mask) == 0 && width <= 8) { width++; mask >>= 1; }
            return width;
        }

        private static byte[] Slice(byte[] d, int start, int end)
        {
            int len = Math.Max(0, Math.Min(end, d.Length) - Math.Max(0, start));
            var result = new byte[len];
            if (len > 0) Buffer.BlockCopy(d, Math.Max(0, start), result, 0, len);
            return result;
        }

        private static byte[] Concat(params byte[][] arrays)
        {
            int total = 0;
            foreach (var a in arrays) total += a.Length;
            var result = new byte[total];
            int off = 0;
            foreach (var a in arrays) { Buffer.BlockCopy(a, 0, result, off, a.Length); off += a.Length; }
            return result;
        }
    }
}
