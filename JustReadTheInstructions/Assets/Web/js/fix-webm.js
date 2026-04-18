const EBML = {
    EBML_ID: 0x1A45DFA3,
    SEGMENT: 0x18538067,
    SEG_INFO: 0x1549A966,
    DURATION: 0x4489,
    TIMECODE_SCALE: 0x2AD7B1,
    CLUSTER: 0x1F43B675,
    CLUSTER_TIMECODE: 0xE7,
    SIMPLE_BLOCK: 0xA3,
    BLOCK_GROUP: 0xA0,
    BLOCK: 0xA1,
    CUES: 0x1C53BB6B,
    CUE_POINT: 0xBB,
    CUE_TIME: 0xB3,
    CUE_TRACK_POSITIONS: 0xB7,
    CUE_TRACK: 0xF7,
    CUE_CLUSTER_POS: 0xF1,
    VOID: 0xEC,
};

function readVint(d, offset) {
    const b = d[offset];
    if (b === 0) return null;
    let width = 1;
    let mask = 0x80;
    while (!(b & mask) && width <= 8) { width++; mask >>= 1; }
    if (width > 8) return null;
    let val = b & (mask - 1);
    for (let i = 1; i < width; i++) val = (val * 256) + d[offset + i];
    return { val, len: width };
}

function readId(d, offset) {
    const b = d[offset];
    if (b === 0) return null;
    let width = 1;
    let mask = 0x80;
    while (!(b & mask) && width <= 4) { width++; mask >>= 1; }
    let id = b;
    for (let i = 1; i < width; i++) id = (id * 256) + d[offset + i];
    return { id, len: width };
}

function readSize(d, offset) {
    return readVint(d, offset);
}

function writeVintFixed(val, width) {
    const b = new Uint8Array(width);
    const marker = 0x80 >> (width - 1);
    let v = val;
    for (let i = width - 1; i > 0; i--) { b[i] = v & 0xFF; v = Math.floor(v / 256); }
    b[0] = (v & (marker - 1)) | marker;
    return b;
}

function writeUint(val, byteLen) {
    const b = new Uint8Array(byteLen);
    let v = val;
    for (let i = byteLen - 1; i >= 0; i--) { b[i] = v & 0xFF; v = Math.floor(v / 256); }
    return b;
}

function writeId(id) {
    if (id <= 0xFF) return new Uint8Array([id]);
    if (id <= 0xFFFF) return new Uint8Array([id >> 8, id & 0xFF]);
    if (id <= 0xFFFFFF) return new Uint8Array([id >> 16, (id >> 8) & 0xFF, id & 0xFF]);
    return new Uint8Array([(id >>> 24) & 0xFF, (id >> 16) & 0xFF, (id >> 8) & 0xFF, id & 0xFF]);
}

function encodeFloat64(v) {
    const buf = new ArrayBuffer(8);
    new DataView(buf).setFloat64(0, v, false);
    return new Uint8Array(buf);
}

function buildElement(id, payload) {
    const idBytes = writeId(id);
    const sizeBytes = writeVintFixed(payload.length, vintWidth(payload.length));
    const out = new Uint8Array(idBytes.length + sizeBytes.length + payload.length);
    out.set(idBytes, 0);
    out.set(sizeBytes, idBytes.length);
    out.set(payload, idBytes.length + sizeBytes.length);
    return out;
}

function vintWidth(val) {
    if (val < 0x7F) return 1;
    if (val < 0x3FFF) return 2;
    if (val < 0x1FFFFF) return 3;
    if (val < 0x0FFFFFFF) return 4;
    return 8;
}

function concat(...arrays) {
    const total = arrays.reduce((s, a) => s + a.length, 0);
    const out = new Uint8Array(total);
    let off = 0;
    for (const a of arrays) { out.set(a, off); off += a.length; }
    return out;
}

function scanSegment(d, segStart, segDataStart, segDataEnd) {
    const clusters = [];
    let segInfoStart = -1, segInfoEnd = -1;
    let cuesStart = -1, cuesEnd = -1;
    let timecodeScale = 1000000;

    let i = segDataStart;
    while (i < segDataEnd) {
        const idR = readId(d, i);
        if (!idR) break;
        const szR = readSize(d, i + idR.len);
        if (!szR) break;

        const eStart = i;
        const dataStart = i + idR.len + szR.len;
        const eEnd = dataStart + szR.val;

        if (idR.id === EBML.SEG_INFO) {
            segInfoStart = eStart;
            segInfoEnd = eEnd;
            let j = dataStart;
            while (j < eEnd) {
                const iR2 = readId(d, j);
                if (!iR2) break;
                const sR2 = readSize(d, j + iR2.len);
                if (!sR2) break;
                if (iR2.id === EBML.TIMECODE_SCALE) {
                    let tv = 0;
                    for (let k = 0; k < sR2.val; k++) tv = tv * 256 + d[j + iR2.len + sR2.len + k];
                    timecodeScale = tv || 1000000;
                }
                j += iR2.len + sR2.len + sR2.val;
            }
        } else if (idR.id === EBML.CLUSTER) {
            let clusterTime = 0;
            let j = dataStart;
            while (j < eEnd) {
                const iR2 = readId(d, j);
                if (!iR2) break;
                const sR2 = readSize(d, j + iR2.len);
                if (!sR2) break;
                if (iR2.id === EBML.CLUSTER_TIMECODE) {
                    let tv = 0;
                    for (let k = 0; k < sR2.val; k++) tv = tv * 256 + d[j + iR2.len + sR2.len + k];
                    clusterTime = tv;
                    break;
                }
                j += iR2.len + sR2.len + sR2.val;
            }
            clusters.push({ pos: eStart - segDataStart, time: clusterTime, end: eEnd });
        } else if (idR.id === EBML.CUES) {
            cuesStart = eStart;
            cuesEnd = eEnd;
        }

        i = eEnd;
    }

    return { clusters, segInfoStart, segInfoEnd, cuesStart, cuesEnd, timecodeScale };
}

function buildCues(clusters, segContentOffset) {
    const cuePoints = clusters.map(c => {
        const timePay = writeUint(c.time, vintWidth(c.time));
        const trackPay = writeUint(1, 1);
        const adjustedPos = c.pos - segContentOffset;
        const posPay = writeUint(adjustedPos < 0 ? 0 : adjustedPos, Math.max(1, Math.ceil(Math.log2(adjustedPos + 1) / 8)));
        const trackPos = concat(
            buildElement(EBML.CUE_TRACK, trackPay),
            buildElement(EBML.CUE_CLUSTER_POS, writeUint(c.pos, 8))
        );
        return buildElement(EBML.CUE_POINT, concat(
            buildElement(EBML.CUE_TIME, writeUint(c.time, timePay.length)),
            buildElement(EBML.CUE_TRACK_POSITIONS, trackPos)
        ));
    });
    return buildElement(EBML.CUES, concat(...cuePoints));
}

function patchSegInfo(d, segInfoStart, segInfoEnd, duration) {
    const durationEl = buildElement(EBML.DURATION, encodeFloat64(duration));

    const oldInfo = d.subarray(segInfoStart, segInfoEnd);
    const idR = readId(d, segInfoStart);
    const szR = readSize(d, segInfoStart + idR.len);
    const dataStart = segInfoStart + idR.len + szR.len;

    let hasDuration = false;
    let j = dataStart;
    while (j < segInfoEnd) {
        const iR2 = readId(d, j);
        if (!iR2) break;
        const sR2 = readSize(d, j + iR2.len);
        if (!sR2) break;
        if (iR2.id === EBML.DURATION) { hasDuration = true; break; }
        j += iR2.len + sR2.len + sR2.val;
    }

    if (hasDuration) return null;

    const oldPayload = d.subarray(dataStart, segInfoEnd);
    const newPayload = concat(oldPayload, durationEl);
    return buildElement(EBML.SEG_INFO, newPayload);
}

export async function fixWebm(blob) {
    const buf = await blob.arrayBuffer();
    const d = new Uint8Array(buf);

    let i = 0;
    const idR = readId(d, i);
    if (!idR || idR.id !== EBML.EBML_ID) return blob;
    const szR = readSize(d, i + idR.len);
    if (!szR) return blob;
    const ebmlEnd = i + idR.len + szR.len + szR.val;

    i = ebmlEnd;
    const segIdR = readId(d, i);
    if (!segIdR || segIdR.id !== EBML.SEGMENT) return blob;
    const segSzR = readSize(d, i + segIdR.len);
    if (!segSzR) return blob;

    const segDataStart = i + segIdR.len + segSzR.len;
    const segDataEnd = segSzR.val === 0x00FFFFFFFFFFFFFF
        ? d.length
        : segDataStart + segSzR.val;

    const { clusters, segInfoStart, segInfoEnd, cuesStart, cuesEnd, timecodeScale } = scanSegment(d, i, segDataStart, segDataEnd);

    if (clusters.length === 0 || segInfoStart < 0) return blob;

    let lastTime = clusters[clusters.length - 1].time;
    for (let ci = clusters[clusters.length - 1].pos + segDataStart; ci < Math.min(clusters[clusters.length - 1].end, d.length);) {
        const iR2 = readId(d, ci);
        if (!iR2) break;
        const sR2 = readSize(d, ci + iR2.len);
        if (!sR2) break;
        if (iR2.id === EBML.SIMPLE_BLOCK || iR2.id === EBML.BLOCK) {
            const vtR = readVint(d, ci + iR2.len + sR2.len);
            if (vtR) {
                const relTime = (d[ci + iR2.len + sR2.len + vtR.len] << 8) | d[ci + iR2.len + sR2.len + vtR.len + 1];
                const signedTime = relTime >= 32768 ? relTime - 65536 : relTime;
                const absTime = clusters[clusters.length - 1].time + signedTime;
                if (absTime > lastTime) lastTime = absTime;
            }
        }
        ci += iR2.len + sR2.len + sR2.val;
    }

    const durationTick = lastTime + 33;
    const newInfoEl = patchSegInfo(d, segInfoStart, segInfoEnd, durationTick);

    const hasCues = cuesStart >= 0;

    if (!newInfoEl && hasCues) return blob;

    const cuesEl = buildCues(clusters, 0);

    const parts = [];
    parts.push(d.subarray(0, segInfoStart));
    parts.push(newInfoEl || d.subarray(segInfoStart, segInfoEnd));

    const afterInfo = newInfoEl ? segInfoEnd : segInfoEnd;

    if (hasCues) {
        parts.push(d.subarray(afterInfo, cuesStart));
        parts.push(cuesEl);
        parts.push(d.subarray(cuesEnd));
    } else {
        parts.push(d.subarray(afterInfo, segDataStart + clusters[0].pos));
        parts.push(cuesEl);
        parts.push(d.subarray(segDataStart + clusters[0].pos));
    }

    const result = concat(...parts);
    return new Blob([result], { type: blob.type });
}
