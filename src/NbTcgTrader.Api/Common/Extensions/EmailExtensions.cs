using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using NbTcgTrader.Api.Common.Email;

namespace NbTcgTrader.Api.Common.Extensions;

/// <summary>
/// Transactional email wiring (issue #69): the <see cref="EmailOptions"/> binding,
/// the provider-backed <see cref="IEmailSender"/>, and the background outbox that
/// keeps provider latency off the request path.
/// </summary>
/// <remarks>
/// The sender is chosen at startup from <c>Email:Provider</c> and short-circuited by
/// the <c>Email:Enabled</c> kill switch, so no slice ever branches on the provider.
/// Defaults are development-safe: the file-drop sender writes messages to disk and
/// sends nothing until a deployment opts into Resend.
/// </remarks>
public static class EmailExtensions
{
    public static IServiceCollection AddApiEmail(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .Validate(
                o => Uri.TryCreate(o.FrontendBaseUrl, UriKind.Absolute, out var uri)
                     && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps),
                "Email:FrontendBaseUrl must be an absolute HTTP(S) URL.")
            .Validate(
                o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _),
                "Email:BaseUrl must be an absolute URL.")
            .Validate(
                o => o.BaseUrl.EndsWith('/'),
                "Email:BaseUrl must end with a trailing slash ('/').")
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.FromAddress) && o.FromAddress.Contains('@'),
                "Email:FromAddress must be an email address.")
            .Validate(
                o => o.TimeoutSeconds > 0,
                "Email:TimeoutSeconds must be positive.")
            .Validate(
                o => o.QueueCapacity > 0,
                "Email:QueueCapacity must be positive.")
            .Validate(
                // Catch the deploy that turns Resend on and forgets the key, at boot
                // rather than at the first password reset a user asks for.
                o => !o.Enabled
                     || o.Provider != EmailProvider.Resend
                     || !string.IsNullOrWhiteSpace(o.ApiKey),
                "Email:ApiKey is required when Email:Provider is Resend and email is enabled.")
            .ValidateOnStart();

        services.AddSingleton<EmailLinkBuilder>();

        // Both concrete senders are registered; the resolved IEmailSender picks one.
        services.AddScoped<FileDropEmailSender>();
        services.AddScoped<DisabledEmailSender>();

        services.AddHttpClient<ResendEmailSender>((serviceProvider, http) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<EmailOptions>>().Value;

            http.BaseAddress = new Uri(options.BaseUrl);
            http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", options.ApiKey);
            }
        });

        services.AddScoped<IEmailSender>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<EmailOptions>>().Value;

            if (!options.Enabled)
            {
                return serviceProvider.GetRequiredService<DisabledEmailSender>();
            }

            return options.Provider switch
            {
                EmailProvider.Resend => serviceProvider.GetRequiredService<ResendEmailSender>(),
                _ => serviceProvider.GetRequiredService<FileDropEmailSender>(),
            };
        });

        // Outbox + drain loop: enqueue is synchronous and non-blocking, delivery is not.
        services.AddSingleton<EmailOutbox>();
        services.AddSingleton<IEmailDispatcher>(sp => sp.GetRequiredService<EmailOutbox>());
        services.AddHostedService<EmailBackgroundService>();

        return services;
    }
}
