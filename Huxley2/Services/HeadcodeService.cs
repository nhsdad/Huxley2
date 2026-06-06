// Calling At - HeadcodeService
// Fetches trainid (headcode) from Rail Data Marketplace staff API
// and provides a lookup dictionary keyed by "HH:mm|DESTCRS"
 
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
 
namespace Huxley2.Services
{
    public class HeadcodeService
    {
        private readonly ILogger<HeadcodeService> _logger;
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
 
        // Cache: key = "CRS|HH:mm_offset", value = (dictionary, expiry)
        private readonly Dictionary<string, (Dictionary<string, string> data, DateTime expiry)> _cache = new();
        private readonly SemaphoreSlim _cacheLock = new(1, 1);
        private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);
 
        private const string BaseUrl = "https://api1.raildata.org.uk/1010-live-departure-board---staff-version1_0/LDBSVWS/api/20220120";
 
        public HeadcodeService(
            ILogger<HeadcodeService> logger,
            HttpClient httpClient,
            IConfiguration config)
        {
            _logger = logger;
            _httpClient = httpClient;
            _apiKey = config["DarwinStaffAccessToken"] ?? string.Empty;
        }
 
        /// <summary>
        /// Returns a dictionary of "HH:mm|DESTCRS" -> trainid for the given station and time offset.
        /// Returns empty dictionary if API key is not configured or call fails.
        /// </summary>
        public async Task<Dictionary<string, string>> GetHeadcodesAsync(string crs, int timeOffset = 0)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                _logger.LogWarning("HeadcodeService: DarwinStaffAccessToken not configured, skipping headcode lookup");
                return new Dictionary<string, string>();
            }

            _logger.LogWarning($"HeadcodeService: fetching headcodes for {crs} with key length {_apiKey.Length}");
 
            var cacheKey = $"{crs}|{timeOffset}";
 
            await _cacheLock.WaitAsync();
            try
            {
                if (_cache.TryGetValue(cacheKey, out var cached) && cached.expiry > DateTime.UtcNow)
                {
                    return cached.data;
                }
            }
            finally
            {
                _cacheLock.Release();
            }
 
            var result = await FetchHeadcodesAsync(crs, timeOffset);
 
            await _cacheLock.WaitAsync();
            try
            {
                _cache[cacheKey] = (result, DateTime.UtcNow.Add(CacheDuration));
            }
            finally
            {
                _cacheLock.Release();
            }
 
            return result;
        }
 
        private async Task<Dictionary<string, string>> FetchHeadcodesAsync(string crs, int timeOffset)
        {
            var result = new Dictionary<string, string>();
 
            try
            {
                var now = DateTime.Now.AddMinutes(timeOffset);
                var timeParam = now.ToString("yyyyMMddTHHmmss");
                var url = $"{BaseUrl}/GetDepBoardWithDetails/{crs.ToUpper()}/{timeParam}";
 
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("x-apikey", _apiKey);
                _logger.LogWarning($"HeadcodeService: calling {url}");
                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning($"HeadcodeService: API returned {response.StatusCode} for {crs}");
                    return result;
                }
 
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
 
                if (!doc.RootElement.TryGetProperty("trainServices", out var trainServices) ||
                    trainServices.ValueKind != JsonValueKind.Array)
                {
                    return result;
                }
 
                foreach (var service in trainServices.EnumerateArray())
                {
                    // Get trainid
                    if (!service.TryGetProperty("trainid", out var trainidProp)) continue;
                    var trainid = trainidProp.GetString();
                    if (string.IsNullOrWhiteSpace(trainid)) continue;
 
                    // Get scheduled departure time — format is "2026-06-06T12:00:00"
                    if (!service.TryGetProperty("std", out var stdProp)) continue;
                    var stdStr = stdProp.GetString();
                    if (string.IsNullOrWhiteSpace(stdStr)) continue;
                    if (!DateTime.TryParse(stdStr, out var std)) continue;
                    var stdKey = std.ToString("HH:mm");
 
                    // Get destination CRS
                    if (!service.TryGetProperty("destination", out var destinations)) continue;
                    if (destinations.ValueKind != JsonValueKind.Array) continue;
                    var destEnum = destinations.EnumerateArray();
                    if (!destEnum.MoveNext()) continue;
                    var firstDest = destEnum.Current;
                    if (!firstDest.TryGetProperty("crs", out var destCrsProp)) continue;
                    var destCrs = destCrsProp.GetString();
                    if (string.IsNullOrWhiteSpace(destCrs)) continue;
 
                    var key = $"{stdKey}|{destCrs}";
                    result.TryAdd(key, trainid);
                }
 
                _logger.LogWarning($"HeadcodeService: fetched {result.Count} headcodes for {crs}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"HeadcodeService: failed to fetch headcodes for {crs}");
            }
 
            return result;
        }
    }
}
