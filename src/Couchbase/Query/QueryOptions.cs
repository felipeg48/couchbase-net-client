using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Couchbase.Core.DataMapping;
using Couchbase.Utils;
using Newtonsoft.Json;

namespace Couchbase.Query
{
    public class QueryOptions
    {
        private string _statement;
        private QueryPlan _preparedPayload;
        private TimeSpan _timeOut = TimeSpan.FromMilliseconds(75000);
        private bool? _readOnly;
        private bool? _includeMetrics;
        private readonly Dictionary<string, object> _parameters = new Dictionary<string, object>();
        private readonly List<object> _arguments = new List<object>();
        private QueryScanConsistency? _scanConsistency;
        private TimeSpan? _scanWait;
        private string _clientContextId;
        private bool _prepareEncoded;
        private bool _adHoc = true;
        private int? _maxServerParallelism;
        private Dictionary<string, Dictionary<string, List<object>>> _scanVectors;
        private int? _scanCapacity;
        private int? _pipelineBatch;
        private int? _pipelineCapacity;
        private readonly Dictionary<string, object> _rawParameters = new Dictionary<string, object>();
        private QueryProfile _profile = QueryProfile.Off;
        private bool _autoExecute;

        public const string ForwardSlash = "/";
        public const string QueryOperator = "?";
        private const string QueryArgPattern = "{0}={1}&";
        public const string TimeoutArgPattern = "{0}={1}ms&";

        internal CancellationToken Token { get; set; } = System.Threading.CancellationToken.None;
        internal TimeSpan TimeoutValue => _timeOut;

        public QueryOptions()
        {
        }

        public QueryOptions(string statement) : this()
        {
            _statement = statement;
            _preparedPayload = null;
            _prepareEncoded = false;
        }

        public QueryOptions(QueryPlan plan, string originalStatement) : this()
        {
            _statement = originalStatement;
            _preparedPayload = plan;
            _prepareEncoded = true;
        }

        private struct QueryParameters
        {
            public const string Statement = "statement";
            public const string PreparedEncoded = "encoded_plan";
            public const string Prepared = "prepared";
            public const string Timeout = "timeout";
            public const string Readonly = "readonly";
            public const string Metrics = "metrics";
            public const string Args = "args";

            // ReSharper disable once UnusedMember.Local
            public const string BatchArgs = "batch_args";

            // ReSharper disable once UnusedMember.Local
            public const string BatchNamedArgs = "batch_named_args";
            public const string Format = "format";
            public const string Encoding = "encoding";
            public const string Compression = "compression";
            public const string Signature = "signature";
            public const string ScanConsistency = "scan_consistency";
            public const string ScanVectors = "scan_vectors";
            public const string ScanWait = "scan_wait";
            public const string Pretty = "pretty";
            public const string Creds = "creds";
            public const string ClientContextId = "client_context_id";
            public const string MaxServerParallelism = "max_parallelism";
            public const string ScanCapacity = "scan_cap";
            public const string PipelineBatch = "pipeline_batch";
            public const string PipelineCapacity = "pipeline_cap";
            public const string Profile = "profile";
            public const string AutoExecute = "auto_execute";
        }

        /// <summary>
        /// Returns true if the request is a prepared statement
        /// </summary>
        public bool IsPrepared => _prepareEncoded;

        /// <summary>
        /// Gets a value indicating whether this query statement is to executed in an ad-hoc manner.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is ad-hoc; otherwise, <c>false</c>.
        /// </value>
        public bool IsAdHoc => _adHoc;

        /// <summary>
        /// Gets the context identifier for the N1QL query request/response. Useful for debugging.
        /// </summary>
        /// <remarks>This value changes for every request./></remarks>
        /// <value>
        /// The context identifier.
        /// </value>
        public string CurrentContextId => _clientContextId;

        /// <summary>
        /// Custom <see cref="IDataMapper"/> to use when deserializing query results.
        /// </summary>
        /// <remarks>Null will use the default <see cref="IDataMapper"/>.</remarks>
        public IDataMapper DataMapper { get; set; }

        /// <summary>
        /// Provides a means of ensuring "read your own writes" or RYOW consistency on the current query.
        /// </summary>
#pragma warning disable 618
        /// <remarks>Note: <see cref="ScanConsistency"/> will be overwritten to <see cref="Query.ScanConsistency.AtPlus"/>.</remarks>
#pragma warning restore 618
        /// <param name="mutationState">State of the mutation.</param>
        /// <returns>A reference to the current <see cref="QueryOptions"/> for method chaining.</returns>
        public QueryOptions ConsistentWith(MutationState mutationState)
        {
#pragma warning disable 618
            ScanConsistency(QueryScanConsistency.AtPlus);
#pragma warning restore 618
            _scanVectors = new Dictionary<string, Dictionary<string, List<object>>>();
            foreach (var token in mutationState)
            {
                if (_scanVectors.TryGetValue(token.BucketRef, out var vector))
                {
                    var bucketId = token.VBucketId.ToString();
                    if (vector.TryGetValue(bucketId, out var bucketRef))
                    {
                        if ((long) bucketRef.First() < token.SequenceNumber)
                        {
                            vector[bucketId] = new List<object>
                            {
                                token.SequenceNumber,
                                token.VBucketUuid.ToString()
                            };
                        }
                    }
                    else
                    {
                        vector.Add(token.VBucketId.ToString(),
                            new List<object>
                            {
                                token.SequenceNumber,
                                token.VBucketUuid.ToString()
                            });
                    }
                }
                else
                {
                    _scanVectors.Add(token.BucketRef, new Dictionary<string, List<object>>
                    {
                        {
                            token.VBucketId.ToString(),
                            new List<object>
                            {
                                token.SequenceNumber,
                                token.VBucketUuid.ToString()
                            }
                        }
                    });
                }
            }

            return this;
        }

        /// <summary>
        /// Specifies the maximum parallelism for the query. A zero or negative value means the number of logical
        /// cpus will be used as the parallelism for the query. There is also a server wide max_parallelism parameter
        /// which defaults to 1. If a request includes max_parallelism, it will be capped by the server max_parallelism.
        /// If a request does not include max_parallelism, the server wide max_parallelism will be used.
        /// </summary>
        /// <param name="parallelism"></param>
        /// <returns></returns>
        /// <value>
        /// The maximum server parallelism.
        /// </value>
        public QueryOptions MaxServerParallelism(int parallelism)
        {
            _maxServerParallelism = parallelism;
            return this;
        }

        /// <summary>
        /// If set to false, the client will try to perform optimizations
        /// transparently based on the server capabilities, like preparing the statement and
        /// then executing a query plan instead of the raw query.
        /// </summary>
        /// <param name="adHoc">if set to <c>false</c> the query will be optimized if possible.</param>
        /// <returns></returns>
        /// <remarks>
        /// The default is <c>true</c>; the query will executed in an ad-hoc manner,
        /// without special optimizations.
        /// </remarks>
        public QueryOptions AdHoc(bool adHoc)
        {
            _adHoc = adHoc;
            return this;
        }

        /// <summary>
        ///  Sets a N1QL statement to be executed in an optimized way using the given queryPlan.
        /// </summary>
        /// <param name="preparedPlan">The <see cref="Query.QueryPlan"/> that was prepared beforehand.</param>
        /// <param name="originalStatement">The original statement (eg. SELECT * FROM default) that the user attempted to optimize</param>
        /// <returns>A reference to the current <see cref="QueryOptions"/> for method chaining.</returns>
        /// <remarks>Required if statement not provided, will erase a previously set Statement.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="preparedPlan"/> is <see langword="null" />.</exception>
        public QueryOptions Prepared(QueryPlan preparedPlan, string originalStatement)
        {
            if (string.IsNullOrWhiteSpace(originalStatement))
            {
                throw new ArgumentNullException(nameof(originalStatement));
            }

            _statement = originalStatement;
            _preparedPayload = preparedPlan ?? throw new ArgumentNullException(nameof(preparedPlan));
            _prepareEncoded = true;
            return this;
        }

        /// <summary>
        /// Sets a N1QL statement to be executed.
        /// </summary>
        /// <param name="statement">Any valid N1QL statement for a POST request, or a read-only N1QL statement (SELECT, EXPLAIN) for a GET request.</param>
        /// <returns>
        /// A reference to the current <see cref="QueryOptions" /> for method chaining.
        /// </returns>
        /// <exception cref="System.ArgumentNullException">statement</exception>
        /// <remarks>
        /// Will erase a previous optimization of a statement using Prepared.
        /// </remarks>
        internal QueryOptions Statement(string statement)
        {
            if (string.IsNullOrWhiteSpace(statement)) throw new ArgumentNullException(nameof(statement));
            _statement = statement;
            _preparedPayload = null;
            _prepareEncoded = false;
            return this;
        }

        /// <summary>
        /// Sets the maximum time to spend on the request.
        /// </summary>
        /// <param name="timeOut">Maximum time to spend on the request</param>
        /// <returns>
        /// A reference to the current <see cref="QueryOptions" /> for method chaining.
        /// </returns>
        /// <remarks>
        /// Optional - the default is 0ms, which means the request runs for as long as it takes.
        /// </remarks>
        public QueryOptions Timeout(TimeSpan timeOut)
        {
            _timeOut = timeOut;
            return this;
        }

        /// <summary>
        /// If a GET request, this will always be true otherwise false.
        /// </summary>
        /// <param name="readOnly">True for get requests.</param>
        /// <returns>
        /// A reference to the current <see cref="QueryOptions" /> for method chaining.
        /// </returns>
        /// <remarks>
        /// Any value set here will be overridden by the type of request sent.
        /// </remarks>
        public QueryOptions ReadOnly(bool readOnly)
        {
            _readOnly = readOnly;
            return this;
        }

        internal bool IsReadOnly => _readOnly.HasValue && _readOnly.Value;

        /// <summary>
        /// Specifies that metrics should be returned with query results.
        /// </summary>
        /// <param name="includeMetrics">True to return query metrics.</param>
        /// <returns>
        /// A reference to the current <see cref="QueryOptions" /> for method chaining.
        /// </returns>
        /// <remarks>
        /// Optional.
        /// </remarks>
        public QueryOptions Metrics(bool includeMetrics)
        {
            _includeMetrics = includeMetrics;
            return this;
        }

        /// <summary>
        /// Adds a named parameter to the parameters to the statement or prepared statement.
        /// </summary>
        /// <param name="name">The name of the parameter.</param>
        /// <param name="value">The value of the parameter.</param>
        /// <returns>
        /// A reference to the current <see cref="QueryOptions" /> for method chaining.
        /// </returns>
        /// <remarks>
        /// Optional.
        /// </remarks>
        public QueryOptions Parameter(string name, object value)
        {
            _parameters.Add(name, value);
            return this;
        }

        /// <summary>
        /// Adds a positional parameter to the parameters to the statement or prepared statement.
        /// </summary>
        /// <param name="value">The value of the positional parameter.</param>
        /// <returns>
        /// A reference to the current <see cref="QueryOptions" /> for method chaining.
        /// </returns>
        /// <remarks>
        /// Optional.
        /// </remarks>
        public QueryOptions Parameter(object value)
        {
            _arguments.Add(value);
            return this;
        }

        /// <summary>
        /// Adds a collection of named parameters to the parameters to the statement or prepared statement.
        /// </summary>
        /// <param name="parameters">A list of <see cref="KeyValuePair{K, V}" /> to be sent.</param>
        /// <returns>
        /// A reference to the current <see cref="QueryOptions" /> for method chaining.
        /// </returns>
        /// <remarks>
        /// Optional.
        /// </remarks>
        public QueryOptions Parameter(params KeyValuePair<string, object>[] parameters)
        {
            if (_arguments.Any())
            {
                throw new ArgumentException("Cannot combine positional and named query parameters.");
            }

            foreach (var parameter in parameters)
            {
                _parameters.Add(parameter.Key, parameter.Value);
            }

            return this;
        }

        /// <summary>
        /// Adds a list of positional parameters to the statement or prepared statement.
        /// </summary>
        /// <param name="parameters">A list of positional parameters.</param>
        /// <returns></returns>
        public QueryOptions Parameter(params object[] parameters)
        {
            if (_parameters.Any())
            {
                throw new ArgumentException("Cannot combine positional and named query parameters.");
            }

            foreach (var parameter in parameters)
            {
                _arguments.Add(parameter);
            }

            return this;
        }

        /// <summary>
        /// Specifies the consistency guarantee/constraint for index scanning.
        /// </summary>
        /// <param name="scanConsistency">Specify the consistency guarantee/constraint for index scanning.</param>
        /// <returns>
        /// A reference to the current <see cref="QueryOptions" /> for method chaining.
        /// </returns>
        /// <exception cref="NotSupportedException">StatementPlus are not currently supported by CouchbaseServer.</exception>
        /// <remarks>
        /// Optional.
        /// </remarks>
        public QueryOptions ScanConsistency(QueryScanConsistency scanConsistency)
        {
#pragma warning disable 618
            if (scanConsistency == QueryScanConsistency.StatementPlus)
#pragma warning restore 618
            {
                throw new NotSupportedException(
                    "AtPlus and StatementPlus are not currently supported by CouchbaseServer.");
            }

            _scanConsistency = scanConsistency;
            return this;
        }

        /// <summary>
        /// Specifies the maximum time the client is willing to wait for an index to catch up to the vector timestamp in the request. If an index has to catch up, and the <see cref="ScanWait" /> time is exceed doing so, an error is returned.
        /// </summary>
        /// <param name="scanWait">The maximum time the client is willing to wait for index to catch up to the vector timestamp.</param>
        /// <returns>
        /// A reference to the current <see cref="QueryOptions" /> for method chaining.
        /// </returns>
        /// <remarks>
        /// Optional.
        /// </remarks>
        public QueryOptions ScanWait(TimeSpan scanWait)
        {
            _scanWait = scanWait;
            return this;
        }

        /// <summary>
        /// Client Context ID.
        /// If no client context ID is provided on this option, a UUID is generated and sent
        /// automatically so by default it is always possible to identify a query when debugging.
        /// </summary>
        /// <param name="clientContextId">The client context identifier.</param>
        /// <returns>A reference to the current <see cref="QueryOptions" /> for method chaining.</returns>
        public QueryOptions ClientContextId(string clientContextId)
        {
            //this is seeded in the ctor
            if (clientContextId != null)
            {
                _clientContextId = clientContextId;
            }

            return this;
        }

        /// <summary>
        /// Adds a raw query parameter and value to the query.
        /// NOTE: This is uncommitted and may change in the future.
        /// </summary>
        /// <param name="name">The paramter name.</param>
        /// <param name="value">The parameter value.</param>
        /// <returns>A reference to the current <see cref="QueryOptions" /> for method chaining.</returns>
        public QueryOptions Raw(string name, object value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Parameter name cannot be null or empty.");
            }

            _rawParameters.Add(name, value);
            return this;
        }

        /// <summary>
        /// Sets maximum buffered channel size between the indexer client
        /// and the query service for index scans.
        ///
        /// This parameter controls when to use scan backfill.
        /// Use 0 or a negative number to disable.
        /// </summary>
        /// <param name="capacity">The maximum number of channels.</param>
        /// <returns>
        /// A reference to the current <see cref="QueryOptions" /> for method chaining.
        /// </returns>
        public QueryOptions ScanCap(int capacity)
        {
            _scanCapacity = capacity;
            return this;
        }

        /// <summary>
        /// Sets the number of items execution operators can batch for
        /// fetch from the KV.
        /// </summary>
        /// <param name="batchSize">The maximum number of items.</param>
        /// <returns>
        /// A reference to the current <see cref="QueryOptions" /> for method chaining.
        /// </returns>
        public QueryOptions PipelineBatch(int batchSize)
        {
            _pipelineBatch = batchSize;
            return this;
        }

        /// <summary>
        /// Sets maximum number of items each execution operator can buffer
        /// between various operators.
        /// </summary>
        /// <param name="capacity">The maximum number of items.</param>
        /// <returns>
        /// A reference to the current <see cref="QueryOptions" /> for method chaining.
        /// </returns>
        public QueryOptions PipelineCap(int capacity)
        {
            _pipelineCapacity = capacity;
            return this;
        }

        public QueryOptions Profile(QueryProfile profile)
        {
            _profile = profile;
            return this;
        }

        public QueryOptions CancellationToken(CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            return this;
        }

        internal QueryOptions QueryPlan(QueryPlan queryPlan)
        {
            _preparedPayload = queryPlan;
            return this;
        }

        internal QueryOptions AutoExecute(bool autoExecute)
        {
            _autoExecute = autoExecute;
            return this;
        }

        /// <summary>
        /// Gets a <see cref="IDictionary{K, V}" /> of the name/value pairs to be POSTed to the service.
        /// </summary>
        /// <returns>
        /// The <see cref="IDictionary{K, V}" /> of the name/value pairs to be POSTed to the service.
        /// </returns>
        /// <exception cref="System.ArgumentException">A statement or prepared plan must be provided.</exception>
        /// <remarks>Since values will be POSTed as JSON, here we deal with unencoded typed values
        /// (like ints, Lists, etc...) rather than only strings.</remarks>
        public IDictionary<string, object> GetFormValues()
        {
            if (string.IsNullOrWhiteSpace(_statement) ||
                (_prepareEncoded && _preparedPayload == null))
            {
                throw new ArgumentException("A statement or prepared plan must be provided.");
            }

            //build the map of request parameters
            IDictionary<string, object> formValues = new Dictionary<string, object>();

            if (_maxServerParallelism.HasValue)
            {
                formValues.Add(QueryParameters.MaxServerParallelism, _maxServerParallelism.Value.ToString());
            }

            if (_prepareEncoded)
            {
                formValues.Add(QueryParameters.Prepared, _preparedPayload.Name);

                // don't include empty plan
                if (!string.IsNullOrEmpty(_preparedPayload.EncodedPlan))
                {
                    formValues.Add(QueryParameters.PreparedEncoded, _preparedPayload.EncodedPlan);
                }
            }
            else
            {
                formValues.Add(QueryParameters.Statement, _statement);
            }

            formValues.Add(QueryParameters.Timeout, (uint) _timeOut.TotalMilliseconds + "ms");

            if (_readOnly.HasValue)
            {
                formValues.Add(QueryParameters.Readonly, _readOnly.Value);
            }

            if (_includeMetrics.HasValue)
            {
                formValues.Add(QueryParameters.Metrics, _includeMetrics);
            }

            if (_parameters.Count > 0)
            {
                foreach (var parameter in _parameters)
                {
                    formValues.Add(
                        parameter.Key.Contains("$") ? parameter.Key : "$" + parameter.Key,
                        parameter.Value);
                }
            }

            if (_arguments.Count > 0)
            {
                formValues.Add(QueryParameters.Args, _arguments);
            }

            if (_scanConsistency.HasValue)
            {
                formValues.Add(QueryParameters.ScanConsistency, _scanConsistency.GetDescription());
            }

            if (_scanVectors != null)
            {
#pragma warning disable 618
                if (_scanConsistency != QueryScanConsistency.AtPlus)
#pragma warning restore 618
                {
                    throw new ArgumentException("Only ScanConsistency.AtPlus is supported for this query request.");
                }

                formValues.Add(QueryParameters.ScanVectors, _scanVectors);
            }

            if (_scanWait.HasValue)
            {
                formValues.Add(QueryParameters.ScanWait, $"{(uint) _scanWait.Value.TotalMilliseconds}ms");
            }

            if (_scanCapacity.HasValue)
            {
                formValues.Add(QueryParameters.ScanCapacity, _scanCapacity.Value.ToString());
            }

            if (_pipelineBatch.HasValue)
            {
                formValues.Add(QueryParameters.PipelineBatch, _pipelineBatch.Value.ToString());
            }

            if (_pipelineCapacity.HasValue)
            {
                formValues.Add(QueryParameters.PipelineCapacity, _pipelineCapacity.Value.ToString());
            }

            if (_profile != QueryProfile.Off)
            {
                formValues.Add(QueryParameters.Profile, _profile.ToString().ToLowerInvariant());
            }

            foreach (var parameter in _rawParameters)
            {
                formValues.Add(parameter.Key, parameter.Value);
            }

            if (_autoExecute)
            {
                formValues.Add(QueryParameters.AutoExecute, true);
            }

            formValues.Add(QueryParameters.ClientContextId, CurrentContextId);
            return formValues;
        }

        /// Gets the JSON representation of this query for execution in a POST.
        /// </summary>
        /// <returns>The form values as a JSON object.</returns>
        public string GetFormValuesAsJson()
        {
            var formValues = GetFormValues();
            return JsonConvert.SerializeObject(formValues);
        }

        /// <summary>
        /// Creates a new <see cref="QueryOptions"/> object.
        /// </summary>
        /// <returns></returns>
        public static QueryOptions Create()
        {
            return new QueryOptions();
        }

        /// <summary>
        /// Creates a new <see cref="QueryOptions"/> object with the specified statement.
        /// </summary>
        /// <param name="statement">The statement.</param>
        /// <returns></returns>
        public static QueryOptions Create(string statement)
        {
            return new QueryOptions(statement);
        }

        /// <summary>
        /// Creates a query using the given plan as an optimization for the originalStatement.
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <param name="originalStatement">The original statement, unoptimized.</param>
        /// <returns></returns>
        public static QueryOptions Create(QueryPlan plan, string originalStatement)
        {
            return new QueryOptions(plan, originalStatement);
        }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            string request;
            try
            {
                request = "[" + GetFormValuesAsJson() + "]";
            }
            catch
            {
                request = string.Empty;
            }

            return request;
        }
    }
}
