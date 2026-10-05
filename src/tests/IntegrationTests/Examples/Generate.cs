/*
order: 10
title: Discover authorized VMs
slug: discover-vms

Discover VMs using a separately authorized device access token.
*/

namespace Muse.IntegrationTests;

public partial class Tests
{
    [TestMethod]
    [TestCategory("Live")]
    public async Task Example_DiscoverVms()
    {
        //// The SDK token identifies the integration; this request needs a device access token.
        using var client = GetAuthenticatedClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var response = await client.FetchVmsAsync(xApiVersion: "1.0.0", cancellationToken: timeout.Token);
        response.VmList.Should().NotBeNull();
        //// VM bearer credentials are sensitive. Do not print the response or copy it into logs.
    }
}
