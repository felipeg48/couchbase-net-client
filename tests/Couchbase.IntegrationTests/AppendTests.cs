using System;
using System.Text;
using System.Threading.Tasks;
using Couchbase.IntegrationTests.Fixtures;
using Xunit;

namespace Couchbase.IntegrationTests
{
    public class AppendTests : IClassFixture<ClusterFixture>
    {
        private readonly ClusterFixture _fixture;

        public AppendTests(ClusterFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task Can_append()
        {
            var collection = await _fixture.GetDefaultCollection();
            var key = Guid.NewGuid().ToString();

            try
            {
                await collection.InsertAsync(key, Encoding.UTF8.GetBytes("hello"));
                await collection.Binary.AppendAsync(key, Encoding.UTF8.GetBytes(" world"));

                using (var result = await collection.GetAsync(key))
                {
                    Assert.Equal("hello world", Encoding.UTF8.GetString(result.ContentAs<byte[]>()));
                }
            }
            finally
            {
                await collection.RemoveAsync(key);
            }
        }
    }
}
