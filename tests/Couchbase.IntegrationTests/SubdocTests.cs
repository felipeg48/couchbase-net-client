using System;
using System.Threading.Tasks;
using Couchbase.IntegrationTests.Fixtures;
using Couchbase.KeyValue;
using Newtonsoft.Json;
using Xunit;

namespace Couchbase.IntegrationTests
{
    public class SubdocTests : IClassFixture<ClusterFixture>
    {
        private const string DocumentKey = "document-key";
        private readonly ClusterFixture _fixture;

        public SubdocTests(ClusterFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task Can_Return_Expiry()
        {
            var collection = await _fixture.GetDefaultCollection();
            await collection.UpsertAsync("Can_Return_Expiry()", new {foo = "bar", bar = "foo"}, options =>options.Expiry(TimeSpan.FromHours(1)));

            var result = await collection.GetAsync("Can_Return_Expiry()", options=>options.Expiry());
            Assert.NotNull(result.Expiry);
        }

        [Fact]
        public async Task LookupIn_Can_Return_FullDoc()
        {
            var collection = await _fixture.GetDefaultCollection();
            await collection.UpsertAsync("LookupIn_Can_Return_FullDoc()", new {foo = "bar", bar = "foo"}, options =>options.Expiry(TimeSpan.FromHours(1)));

            var result = await collection.LookupInAsync("LookupIn_Can_Return_FullDoc()", builder=>builder.GetFull());
            var doc = result.ContentAs<dynamic>(0);
            Assert.NotNull(doc);
        }

        [Fact]
        public async Task Can_perform_lookup_in()
        {
            var collection = await _fixture.GetDefaultCollection();
            await collection.UpsertAsync(DocumentKey, new {foo = "bar", bar = "foo"});

            using (var result = await collection.LookupInAsync(DocumentKey, ops =>
            {
                ops.Get("foo");
                ops.Get("bar");
            }))
            {
                Assert.Equal("bar", result.ContentAs<string>(0));
                Assert.Equal("foo", result.ContentAs<string>(1));
            }
        }

        [Fact]
        public async Task Can_do_lookup_in_with_array()
        {
            var collection = await _fixture.GetDefaultCollection();
            await collection.UpsertAsync(DocumentKey, new {foo = "bar", bar = "foo"});

            using (var result = await collection.LookupInAsync(DocumentKey, new[]
            {
                LookupInSpec.Get("foo"),
                LookupInSpec.Get("bar")
            }))
            {
                Assert.Equal("bar", result.ContentAs<string>(0));
                Assert.Equal("foo", result.ContentAs<string>(1));
            }
        }

        [Fact]
        public async Task Can_perform_mutate_in()
        {
            var collection = await _fixture.GetDefaultCollection();
            await collection.UpsertAsync(DocumentKey,  new {foo = "bar", bar = "foo"});

            await collection.MutateInAsync(DocumentKey, ops =>
            {
                ops.Upsert("name", "mike");
                ops.Replace("bar", "bar");
            });

            using (var getResult = await collection.GetAsync(DocumentKey))
            {
                var content = getResult.ContentAs<string>();

                var expected = new
                {
                    foo = "bar",
                    bar = "bar",
                    name = "mike"
                };
                Assert.Equal(JsonConvert.SerializeObject(expected), content);
            }
        }

        [Fact]
        public async Task Can_perform_mutate_in_with_array()
        {
            var collection = await _fixture.GetDefaultCollection();
            await collection.UpsertAsync(DocumentKey, new {foo = "bar", bar = "foo"});

            await collection.MutateInAsync(DocumentKey, new[]
            {
                MutateInSpec.Upsert("name", "mike"),
                MutateInSpec.Replace("bar", "bar")
            });

            using (var getResult = await collection.GetAsync(DocumentKey))
            {
                var content = getResult.ContentAs<string>();

                var expected = new
                {
                    foo = "bar",
                    bar = "bar",
                    name = "mike"
                };
                Assert.Equal(JsonConvert.SerializeObject(expected), content);
            }
        }

        [Fact]
        public async Task Test_When_Connection_Fails_It_Is_Recreated()
        {
            var collection = await _fixture.GetDefaultCollection();

            try
            {
                await collection.LookupInAsync("docId", builder =>
                {
                    builder.Get("doc.path", isXattr: true);
                    builder.Count("path", isXattr: true); //will fail and cause server to close connection
                });
            }
            catch
            {
                // ignored -
                // The code above will force the server to abort the socket;
                // the connection will be reestablished and the code below should succeed
            }

            await collection.UpsertAsync(DocumentKey,  new {foo = "bar", bar = "foo"});;
        }

        [Fact]
        public async Task Test_MutateInAsync_Upsert_And_Xattr_Doc()
        {
            var collection = await _fixture.GetDefaultCollection();

            var result = await collection.MutateInAsync("foo", specs =>
                {
                    specs.Upsert("key", "value", true, true);
                    specs.Upsert("name", "mike");
                },
                options => options.StoreSemantics(StoreSemantics.Upsert));
        }
    }
}
