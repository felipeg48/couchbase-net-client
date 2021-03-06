using System;
using System.Linq;
using System.Threading.Tasks;
using Couchbase.IntegrationTests.Fixtures;
using Xunit;

namespace Couchbase.IntegrationTests.Diagnostics
{
    public class PingReportTests : IClassFixture<ClusterFixture>
    {
        private ICluster _cluster;

        public PingReportTests(ClusterFixture fixture)
        {
            _fixture = fixture;
            _cluster = _fixture.Cluster;
        }

        private readonly ClusterFixture _fixture;

        [Fact]
        public async Task Can_Get_PingReport()
        {
            var bucket = await _fixture.GetDefaultBucket();
            var report = await bucket.PingAsync();

            Assert.NotNull(report);
            Assert.NotEmpty(report.Id);
            Assert.Equal(5, report.Services.Count);
            Assert.Contains(report.Services["kv"], e => e.Type == ServiceType.KeyValue);
            Assert.Contains(report.Services["view"], e => e.Type == ServiceType.Views);
            Assert.Contains(report.Services["n1ql"], e => e.Type == ServiceType.Query);
            Assert.Contains(report.Services["fts"], e => e.Type == ServiceType.Search);
            Assert.Contains(report.Services["cbas"], e=>e.Type == ServiceType.Analytics);
        }

        [Fact]
        public async Task Can_Get_PingReport_With_Custom_Set_Of_Services()
        {
            var services = new[]
            {
                ServiceType.KeyValue,
                ServiceType.Query
            };

            var bucket = await _fixture.GetDefaultBucket();
            var report = await bucket.PingAsync(services);

            Assert.NotNull(report);
            Assert.NotEmpty(report.Id); // verify report Id has been assigned
            Assert.Equal(services.Length, report.Services.Count);

            Assert.True(report.Services["kv"].Any(e => e.Type == ServiceType.KeyValue)); // at least one KV
            Assert.True(report.Services["n1ql"].Any(e => e.Type == ServiceType.Query)); // at least one N1QL

            Assert.False(report.Services.ContainsKey("view"));
            Assert.False(report.Services.ContainsKey("fts"));
            Assert.False(report.Services.ContainsKey("cbas"));
        }

        [Fact]
        public async Task Can_Get_PingReport_With_ReportId()
        {
            const string reportId = "report-id";
            var bucket = await _fixture.GetDefaultBucket();
            var report = await bucket.PingAsync(reportId);

            Assert.NotNull(report);
            Assert.Equal(reportId, report.Id);
            Assert.True(report.Services["kv"].Any(e => e.Type == ServiceType.KeyValue)); // at least one KV
            Assert.True(report.Services["view"].Any(e => e.Type == ServiceType.Views)); // at least one Index
            Assert.True(report.Services["n1ql"].Any(e => e.Type == ServiceType.Query)); // at least one N1QL
            Assert.True(report.Services["fts"].Any(e => e.Type == ServiceType.Search));
            Assert.Contains(report.Services["cbas"], e => e.Type == ServiceType.Analytics);
        }

        [Fact]
        public async Task Can_Get_PingReport_With_Custom_Set_Of_Services_And_ReportId()
        {
            const string reportId = "report-id";
            var services = new[]
            {
                ServiceType.KeyValue,
                ServiceType.Query
            };

            var bucket = await _fixture.GetDefaultBucket();
            var report = await bucket.PingAsync(reportId, services);

            Assert.NotNull(report);
            Assert.Equal(reportId, report.Id);
            Assert.Equal(services.Length, report.Services.Count);

            Assert.True(report.Services["kv"].Any(e => e.Type == ServiceType.KeyValue)); // at least one KV
            Assert.True(report.Services["n1ql"].Any(e => e.Type == ServiceType.Query)); // at least one N1QL

            Assert.False(report.Services.ContainsKey("view"));
            Assert.False(report.Services.ContainsKey("fts"));
            Assert.False(report.Services.ContainsKey("cbas"));
        }
    }
}
