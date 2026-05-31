using System.Collections.Generic;
using JustReadTheInstructions;
using Xunit;

namespace JRTI.Tests
{
    public class JRTICameraRuntimeTests
    {
        public JRTICameraRuntimeTests() => JRTICameraRuntime.Reset();

        [Fact]
        public void PreferredId_IsHonored_WhenFree()
        {
            Assert.Equal(5, JRTICameraRuntime.ResolveId(persistentId: 100, preferredId: 5));
        }

        [Fact]
        public void SamePersistentId_ReturnsCachedId_IgnoringNewPreferred()
        {
            int first = JRTICameraRuntime.ResolveId(100, 1);
            int second = JRTICameraRuntime.ResolveId(100, 9);
            Assert.Equal(first, second);
            Assert.Equal(1, second);
        }

        [Fact]
        public void CollidingPreferredId_BumpsToNextAvailable()
        {
            Assert.Equal(1, JRTICameraRuntime.ResolveId(100, 1));
            Assert.Equal(2, JRTICameraRuntime.ResolveId(200, 1));
        }

        [Fact]
        public void NonPositivePreferredId_FallsBackToLowestAvailable()
        {
            Assert.Equal(1, JRTICameraRuntime.ResolveId(100, 0));
            Assert.Equal(2, JRTICameraRuntime.ResolveId(200, -5));
        }

        [Fact]
        public void NextAvailable_FillsLowestGap()
        {
            JRTICameraRuntime.ResolveId(100, 1);
            JRTICameraRuntime.ResolveId(200, 2);
            JRTICameraRuntime.ResolveId(300, 3);

            JRTICameraRuntime.RetainOnly(new HashSet<uint> { 100, 300 });

            Assert.Equal(2, JRTICameraRuntime.ResolveId(400, 0));
        }

        [Fact]
        public void RetainOnly_EvictsStaleIds_AndKeepsLiveOnes()
        {
            JRTICameraRuntime.ResolveId(100, 1);
            JRTICameraRuntime.ResolveId(200, 2);

            JRTICameraRuntime.RetainOnly(new HashSet<uint> { 100 });

            Assert.Equal(1, JRTICameraRuntime.ResolveId(100, 9));
            Assert.Equal(2, JRTICameraRuntime.ResolveId(200, 2));
        }

        [Fact]
        public void Reset_ClearsAllAssignments()
        {
            JRTICameraRuntime.ResolveId(100, 1);
            JRTICameraRuntime.Reset();
            Assert.Equal(7, JRTICameraRuntime.ResolveId(100, 7));
        }
    }
}
