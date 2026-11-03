using DeadCellsMultiplayerMod.Ghost;
using DeadCellsMultiplayerMod.Tools;
using Xunit;

namespace DeadCellsMultiplayerMod.ProtocolReplay.Tests;

public sealed class CombatAndVisualSyncTests
{
    [Fact]
    public void RemoteAffectEvent_RetainsProperties()
    {
        var evt = new NetNode.RemoteAffectEvent(userId: 5, affectId: 42, sec: 3.5, isRemove: false);
        Assert.Equal(5, evt.UserId);
        Assert.Equal(42, evt.AffectId);
        Assert.Equal(3.5, evt.Sec);
        Assert.False(evt.IsRemove);

        var remEvt = new NetNode.RemoteAffectEvent(userId: 5, affectId: 42, sec: 0.0, isRemove: true);
        Assert.True(remEvt.IsRemove);
    }

    [Theory]
    [InlineData("punch", true)]
    [InlineData("punch_01", true)]
    [InlineData("kick", true)]
    [InlineData("slash", true)]
    [InlineData("fist_combo", true)]
    [InlineData("w_sword_atk1", true)]
    [InlineData("attack_heavy", true)]
    [InlineData("strike_down", true)]
    [InlineData("swing_left", true)]
    [InlineData("smash", true)]
    [InlineData("thrust", true)]
    [InlineData("stab", true)]
    [InlineData("cast_spell", true)]
    [InlineData("throw_grenade", true)]
    [InlineData("fire_arrow", true)]
    [InlineData("dive_slam", true)]
    [InlineData("hit_react", true)]
    [InlineData("idle", false)]
    [InlineData("run", false)]
    [InlineData("jump", false)]
    [InlineData("fall", false)]
    [InlineData("crouch", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void AttackAnimationVocabulary_RecognizesUnarmedAndWeaponAttacks(string? anim, bool expected)
    {
        var result = CombatSyncHelpers.IsAttackAnim(anim!);
        Assert.Equal(expected, result);
    }
}