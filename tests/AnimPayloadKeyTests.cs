using System;
using System.Collections.Generic;
using DeadCellsMultiplayerMod.Mobs.MobsSynchronization;
using Xunit;

namespace DeadCellsMultiplayerMod.ProtocolReplay.Tests;

public sealed class AnimPayloadKeyTests
{
    [Fact]
    public void AnimPayloadKey_EqualityAndHashCode_WorkCorrectly()
    {
        var key1 = new MobsSynchronization.AnimPayloadKey("walk", false, 1.0);
        var key2 = new MobsSynchronization.AnimPayloadKey("walk", false, 1.0);
        var keyDiffGroup = new MobsSynchronization.AnimPayloadKey("run", false, 1.0);
        var keyDiffRev = new MobsSynchronization.AnimPayloadKey("walk", true, 1.0);
        var keyDiffSpeed = new MobsSynchronization.AnimPayloadKey("walk", false, 1.5);

        Assert.True(key1.Equals(key2));
        Assert.True(key1 == key2);
        Assert.False(key1 != key2);
        Assert.Equal(key1.GetHashCode(), key2.GetHashCode());

        Assert.False(key1.Equals(keyDiffGroup));
        Assert.False(key1 == keyDiffGroup);
        Assert.True(key1 != keyDiffGroup);

        Assert.False(key1.Equals(keyDiffRev));
        Assert.False(key1 == keyDiffRev);
        Assert.True(key1 != keyDiffRev);

        Assert.False(key1.Equals(keyDiffSpeed));
        Assert.False(key1 == keyDiffSpeed);
        Assert.True(key1 != keyDiffSpeed);

        Assert.False(key1.Equals((object?)null));
        Assert.False(key1.Equals("not an anim payload key"));
        Assert.True(key1.Equals((object)key2));
    }

    [Fact]
    public void AnimPayloadKey_NullAndNegativeZero_HandledSafely()
    {
        var keyNull = new MobsSynchronization.AnimPayloadKey(null!, false, -0.0);
        var keyEmpty = new MobsSynchronization.AnimPayloadKey(string.Empty, false, 0.0);

        Assert.Equal(string.Empty, keyNull.Group);
        Assert.Equal(0.0, keyNull.Speed);
        Assert.True(keyNull.Equals(keyEmpty));
        Assert.True(keyNull == keyEmpty);
        Assert.Equal(keyNull.GetHashCode(), keyEmpty.GetHashCode());
    }

    [Fact]
    public void AnimPayloadKey_DefaultStruct_HandledSafely()
    {
        var defaultKey = default(MobsSynchronization.AnimPayloadKey);
        var keyEmpty = new MobsSynchronization.AnimPayloadKey(string.Empty, false, 0.0);
        var keyNull = new MobsSynchronization.AnimPayloadKey(null!, false, -0.0);

        Assert.True(defaultKey.Equals(keyEmpty));
        Assert.True(defaultKey.Equals(keyNull));
        Assert.True(keyEmpty.Equals(defaultKey));
        Assert.True(keyNull.Equals(defaultKey));
        Assert.True(defaultKey == keyEmpty);
        Assert.True(defaultKey == keyNull);
        Assert.Equal(defaultKey.GetHashCode(), keyEmpty.GetHashCode());
        Assert.Equal(defaultKey.GetHashCode(), keyNull.GetHashCode());
        Assert.Equal("~0~0", defaultKey.ToString());
    }

    [Fact]
    public void AnimPayloadKey_NaN_HandledConsistently()
    {
        var keyNaN1 = new MobsSynchronization.AnimPayloadKey("atk", false, double.NaN);
        var keyNaN2 = new MobsSynchronization.AnimPayloadKey("atk", false, double.NaN);

        Assert.True(keyNaN1.Equals(keyNaN2));
        Assert.True(keyNaN1 == keyNaN2);
        Assert.Equal(keyNaN1.GetHashCode(), keyNaN2.GetHashCode());
    }

    [Fact]
    public void AnimPayloadKey_ToString_FormatsCorrectly()
    {
        var key1 = new MobsSynchronization.AnimPayloadKey("idle", false, 1.0);
        Assert.Equal("idle~0~1", key1.ToString());

        var key2 = new MobsSynchronization.AnimPayloadKey("walk", true, 1.25);
        Assert.Equal("walk~1~1.25", key2.ToString());

        var keyDefault = default(MobsSynchronization.AnimPayloadKey);
        Assert.Equal("~0~0", keyDefault.ToString());
    }

    [Fact]
    public void AnimPayloadKey_HashSet_Deduplication()
    {
        var set = new HashSet<MobsSynchronization.AnimPayloadKey>
        {
            default,
            new MobsSynchronization.AnimPayloadKey("", false, 0.0),
            new MobsSynchronization.AnimPayloadKey(null!, false, -0.0)
        };

        Assert.Single(set);
    }

    [Fact]
    public void AnimPayloadKey_DictionaryLookup_AndZeroAllocationOnHit()
    {
        var cache = new Dictionary<MobsSynchronization.AnimPayloadKey, string>();
        var sampleKey = new MobsSynchronization.AnimPayloadKey("idle", false, 1.0);
        const string expectedPayload = "idle~0~1";
        cache[sampleKey] = expectedPayload;

        // Verify retrieval
        var lookupKey = new MobsSynchronization.AnimPayloadKey("idle", false, 1.0);
        Assert.True(cache.TryGetValue(lookupKey, out var result));
        Assert.Same(expectedPayload, result);

        // Warm up JIT
        for (int i = 0; i < 100; i++)
        {
            var k = new MobsSynchronization.AnimPayloadKey("idle", false, 1.0);
            cache.TryGetValue(k, out _);
        }

        // Measure allocations across 50,000 dictionary lookups
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50000; i++)
        {
            var k = new MobsSynchronization.AnimPayloadKey("idle", false, 1.0);
            bool found = cache.TryGetValue(k, out var hit);
            if (!found || !ReferenceEquals(hit, expectedPayload))
            {
                throw new InvalidOperationException("Lookup failed");
            }
        }
        long after = GC.GetAllocatedBytesForCurrentThread();

        long allocated = after - before;
        Assert.Equal(0L, allocated);
    }
}
