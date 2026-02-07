
using System.Net.Http.Headers;
using System.Text;
using LcusRelay.Core.Automation;
using LcusRelay.Core.Config;

namespace LcusRelay.Core.Actions;

public sealed class WebhookAction : IAction
{
    private readonly WebhookActionConfig _cfg;
    private static readonly HttpClient _http = new();

    public WebhookAction(WebhookActionConfig cfg)
    {
        _cfg = cfg;
    }

    public async Task ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_cfg.Url))
            return;

        using var req = new HttpRequestMessage(new HttpMethod(_cfg.Method ?? "POST"), _cfg.Url);

        foreach (var (k, v) in _cfg.Headers)
            req.Headers.TryAddWithoutValidation(k, v);

        if (_cfg.Body is not null)
        {
            req.Content = new StringContent(_cfg.Body, Encoding.UTF8, "application/json");
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        using var _ = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
    }
}
