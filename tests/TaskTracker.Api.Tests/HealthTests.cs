using System.Net;
using FluentAssertions;
using TaskTracker.Api.Tests.Infrastructure;

namespace TaskTracker.Api.Tests;

public class HealthTests(TaskTrackerWebAppFactory factory) : IClassFixture<TaskTrackerWebAppFactory>
{
    [Fact]
    public async Task Health_Returns_200()
    {
        var res = await factory.CreateClient().GetAsync("/health");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
