using Odm.Modules.Software.Domain;
using Xunit;

namespace Odm.Tests;

public class RolloutPlanTests
{
    private static RolloutPlan Percent(params (int Days, int Percent)[] stages) =>
        RolloutPlan.Create(RolloutMode.Percent, stages.Select(s => new RolloutStage(s.Days, s.Percent, null))).Plan!;

    [Fact]
    public void Percent_plan_must_rise_and_end_at_100()
    {
        var stages = new[] { new RolloutStage(5, 20, null), new RolloutStage(10, 40, null) };
        Assert.NotNull(RolloutPlan.Create(RolloutMode.Percent, stages).Error);

        var falling = new[] { new RolloutStage(5, 50, null), new RolloutStage(10, 20, null), new RolloutStage(15, 100, null) };
        Assert.NotNull(RolloutPlan.Create(RolloutMode.Percent, falling).Error);

        Assert.Null(RolloutPlan.Create(RolloutMode.Percent,
            [new RolloutStage(5, 20, null), new RolloutStage(10, 40, null), new RolloutStage(15, 100, null)]).Error);
    }

    [Fact]
    public void Group_plan_needs_distinct_groups()
    {
        var g = Guid.NewGuid();
        Assert.NotNull(RolloutPlan.Create(RolloutMode.Groups, [new RolloutStage(0, null, g), new RolloutStage(3, null, g)]).Error);
        Assert.NotNull(RolloutPlan.Create(RolloutMode.Groups, [new RolloutStage(0, 10, g)]).Error);
        Assert.Null(RolloutPlan.Create(RolloutMode.Groups, [new RolloutStage(0, null, g), new RolloutStage(3, null, Guid.NewGuid())]).Error);
    }

    [Fact]
    public void Empty_or_out_of_range_stages_are_rejected()
    {
        Assert.NotNull(RolloutPlan.Create(RolloutMode.Percent, []).Error);
        Assert.NotNull(RolloutPlan.Create(RolloutMode.Percent, [new RolloutStage(-1, 100, null)]).Error);
        Assert.NotNull(RolloutPlan.Create(RolloutMode.Percent, [new RolloutStage(RolloutPlan.MaxDays + 1, 100, null)]).Error);
    }

    [Fact]
    public void Json_round_trip_keeps_plan()
    {
        var plan = Percent((5, 20), (10, 40), (15, 100));
        var parsed = RolloutPlan.Parse(plan.ToJson());

        Assert.NotNull(parsed);
        Assert.Equal(RolloutMode.Percent, parsed!.Mode);
        Assert.Equal(plan.Stages, parsed.Stages);
        Assert.Null(RolloutPlan.Parse(null));
        Assert.Null(RolloutPlan.Parse("  "));
    }

    [Fact]
    public void Nothing_is_released_before_first_stage()
    {
        var plan = Percent((5, 20), (10, 40), (15, 100));
        var assignment = Guid.NewGuid();

        Assert.Equal(0, plan.ReleasedPercent(TimeSpan.FromDays(4)));
        Assert.Equal(0, plan.StagesReached(TimeSpan.FromDays(4)));
        Assert.DoesNotContain(Enumerable.Range(0, 200),
            _ => plan.IsReleased(assignment, Guid.NewGuid(), TimeSpan.FromDays(4)));
    }

    [Theory]
    [InlineData(5, 20, 1)]
    [InlineData(9, 20, 1)]
    [InlineData(10, 40, 2)]
    [InlineData(15, 100, 3)]
    [InlineData(100, 100, 3)]
    public void Stages_release_growing_percent(int days, int percent, int reached)
    {
        var plan = Percent((5, 20), (10, 40), (15, 100));
        var elapsed = TimeSpan.FromDays(days);

        Assert.Equal(percent, plan.ReleasedPercent(elapsed));
        Assert.Equal(reached, plan.StagesReached(elapsed));
    }

    [Fact]
    public void Release_is_stable_and_only_grows()
    {
        var plan = Percent((5, 20), (10, 40), (15, 100));
        var assignment = Guid.NewGuid();
        var devices = Enumerable.Range(0, 500).Select(_ => Guid.NewGuid()).ToList();

        var first = devices.Where(d => plan.IsReleased(assignment, d, TimeSpan.FromDays(5))).ToHashSet();
        var again = devices.Where(d => plan.IsReleased(assignment, d, TimeSpan.FromDays(5))).ToHashSet();
        var later = devices.Where(d => plan.IsReleased(assignment, d, TimeSpan.FromDays(10))).ToHashSet();

        Assert.Equal(first, again);
        Assert.True(first.IsSubsetOf(later));
        Assert.True(later.Count > first.Count);
        Assert.Equal(devices.Count, devices.Count(d => plan.IsReleased(assignment, d, TimeSpan.FromDays(15))));
    }

    [Fact]
    public void Released_share_is_roughly_the_configured_percent()
    {
        var plan = Percent((0, 30), (1, 100));
        var assignment = Guid.NewGuid();
        var released = Enumerable.Range(0, 2000).Count(_ => plan.IsReleased(assignment, Guid.NewGuid(), TimeSpan.Zero));

        Assert.InRange(released, 450, 750); // 30 % von 2000 = 600
    }

    [Fact]
    public void Bucket_is_deterministic_and_within_range()
    {
        var a = Guid.NewGuid();
        var d = Guid.NewGuid();

        Assert.Equal(RolloutPlan.Bucket(a, d), RolloutPlan.Bucket(a, d));
        Assert.All(Enumerable.Range(0, 200), _ => Assert.InRange(RolloutPlan.Bucket(a, Guid.NewGuid()), 0, 99));
    }

    [Fact]
    public void Group_stages_release_members_after_their_delay_and_the_rest_at_the_end()
    {
        var pilot = Guid.NewGuid();
        var broad = Guid.NewGuid();
        var plan = RolloutPlan.Create(RolloutMode.Groups,
            [new RolloutStage(0, null, pilot), new RolloutStage(7, null, broad)]).Plan!;

        var inPilot = Guid.NewGuid();
        var inBroad = Guid.NewGuid();
        var rest = Guid.NewGuid();
        bool Member(Guid group, Guid device) => (group == pilot && device == inPilot) || (group == broad && device == inBroad);
        var assignment = Guid.NewGuid();

        Assert.True(plan.IsReleased(assignment, inPilot, TimeSpan.Zero, Member));
        Assert.False(plan.IsReleased(assignment, inBroad, TimeSpan.FromDays(6), Member));
        Assert.True(plan.IsReleased(assignment, inBroad, TimeSpan.FromDays(7), Member));
        Assert.False(plan.IsReleased(assignment, rest, TimeSpan.FromDays(6), Member));
        Assert.True(plan.IsReleased(assignment, rest, TimeSpan.FromDays(7), Member));
    }
}
