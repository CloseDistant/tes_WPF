namespace RuinaoSoftwareWpf;

using System.Net.Http.Json;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// 网新测试环境外部随访接口客户端。
/// 只使用 .NET 自带 HttpClient，不引入新的 NuGet 或原生依赖。
/// </summary>
public sealed class ExternalFollowUpService : IExternalFollowUpService
{
    public const string TestBaseAddress = "https://mini.insigmamed.com:9161/eams-admin/";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient httpClient;
    private readonly ILoggingService logger;

    public ExternalFollowUpService(ILoggingService logger)
    {
        this.logger = logger;
        httpClient = new HttpClient
        {
            BaseAddress = new Uri(TestBaseAddress),
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    public async Task<ExternalFollowUpPatientPage> SearchPatientsAsync(
        string? phone,
        int pageNumber = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber));
        }

        if (pageSize is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        var request = new
        {
            pageNum = pageNumber,
            pageSize,
            phone = phone?.Trim() ?? string.Empty
        };

        using var response = await httpClient.PostAsJsonAsync(
            "external/followup/pageRecord",
            request,
            JsonOptions,
            cancellationToken).ConfigureAwait(false);

        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<PagePayload>>(
            JsonOptions,
            cancellationToken).ConfigureAwait(false);

        if (payload is null || !payload.Success || payload.Data is null)
        {
            throw new InvalidOperationException(payload?.Message ?? "患者查询接口返回无效数据。");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"患者查询请求失败（HTTP {(int)response.StatusCode}）。");
        }

        logger.Info($"外部患者查询完成：phoneLength={request.phone.Length}，total={payload.Data.Total}");
        return new ExternalFollowUpPatientPage(
            payload.Data.PageNumber,
            payload.Data.PageSize,
            payload.Data.TotalPage,
            payload.Data.Total,
            payload.Data.Items ?? []);
    }

    public async Task<IReadOnlyList<ExternalFollowUpDetail>> GetFollowUpDetailsAsync(
        string phone,
        CancellationToken cancellationToken = default)
    {
        var normalizedPhone = phone?.Trim() ?? string.Empty;
        if (normalizedPhone.Length == 0)
        {
            throw new ArgumentException("查询随访详情必须提供手机号。", nameof(phone));
        }

        using var response = await httpClient.PostAsJsonAsync(
            "external/followup/listDetail",
            new { phone = normalizedPhone },
            JsonOptions,
            cancellationToken).ConfigureAwait(false);

        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<List<ExternalFollowUpDetail>>>(
            JsonOptions,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"随访详情请求失败（HTTP {(int)response.StatusCode}）。");
        }

        if (payload is null || !payload.Success || payload.Data is null)
        {
            throw new InvalidOperationException(payload?.Message ?? "随访详情接口返回无效数据。");
        }

        logger.Info($"外部随访详情查询完成：phoneLength={normalizedPhone.Length}，count={payload.Data.Count}");
        return payload.Data;
    }

    private sealed record ApiResponse<T>(
        bool Success,
        int Code,
        string? Msg,
        T? Data)
    {
        public string? Message => Msg;
    }

    private sealed record PagePayload(
        [property: JsonPropertyName("pageNum")]
        long PageNum,
        [property: JsonPropertyName("pageSize")]
        long PageSize,
        [property: JsonPropertyName("totalPage")]
        long TotalPage,
        [property: JsonPropertyName("total")]
        long Total,
        [property: JsonPropertyName("list")]
        IReadOnlyList<ExternalFollowUpPatient>? List)
    {
        public long PageNumber => PageNum;
        public IReadOnlyList<ExternalFollowUpPatient>? Items => List;
    }
}
