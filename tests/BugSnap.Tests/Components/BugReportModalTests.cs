using Bunit;
using BugSnap;
using BugSnap.Components;
using BugSnap.Destinations;
using BugSnap.Models;
using BugSnap.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BugSnap.Tests.Components;

public class BugReportModalTests : BunitContext
{
    // --- Test doubles (no mock libraries — Rule 7) ---

    private sealed class FakeBugContextCollector(BugContextSnapshot snapshot)
        : BugContextCollector(null!, null!, null!, null!, new BugSnapOptions())
    {
        public override Task<BugContextSnapshot> CollectAsync(CancellationToken ct = default)
            => Task.FromResult(snapshot);
    }

    private sealed class RecordingDestination : IBugReportDestination
    {
        public BugReport? LastReport { get; private set; }
        public string Name => "Recording";

        public Task<BugReportResult> SubmitAsync(BugReport report, CancellationToken ct = default)
        {
            LastReport = report;
            return Task.FromResult(new BugReportResult(true, Name, Url: "https://example.test/issue/1"));
        }
    }

    private (IRenderedComponent<BugReportModal> Cut, RecordingDestination Destination) RenderModal(
        BugContextSnapshot snapshot, BugSnapOptions? options = null)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        options ??= new BugSnapOptions { RateLimitSeconds = 0 };
        var destination = new RecordingDestination();

        Services.AddSingleton(options);
        Services.AddSingleton<IOptions<BugSnapOptions>>(new OptionsWrapper<BugSnapOptions>(options));
        Services.AddSingleton<SeverityDetector>();
        Services.AddSingleton<IBugReportDestination>(destination);
        Services.AddSingleton<MultiDestinationDispatcher>();
        Services.AddSingleton<BugContextCollector>(new FakeBugContextCollector(snapshot));

        var cut = Render<BugReportModal>(p => p.Add(x => x.Visible, true));
        return (cut, destination);
    }

    private static BugContextSnapshot ContextWith5xx() => new()
    {
        CurrentRoute = "/inbox",
        RecentRequests = [new HttpActivityEntry { Method = "GET", Url = "/api/data", StatusCode = 500 }],
        RecentJsErrors = [],
        SuggestedCategory = BugSnapCategory.API
    };

    private static BugContextSnapshot ThinContext() => new()
    {
        CurrentRoute = "/dashboard",
        RecentRequests = [],
        RecentJsErrors = []
    };

    // Context carrying the B1 fields: memory + a console error + a breadcrumb. Detail
    // values are deliberately benign so they survive sanitization unchanged and can be
    // asserted verbatim in the preview JSON.
    private static BugContextSnapshot ContextWithB1Fields() => new()
    {
        CurrentRoute = "/dashboard",
        RecentRequests = [],
        RecentJsErrors = [],
        Memory = new MemoryInfo
        {
            JsHeapUsedBytes = 123456,
            JsHeapTotalBytes = 654321,
            JsHeapLimitBytes = 999999,
            ManagedHeapBytes = 42424242
        },
        RecentConsoleErrors =
        [
            new ConsoleErrorEntry
            {
                Message = "ConsoleBoomMarker",
                Stack = "at doStuff (app.js:10)",
                TimestampUtc = DateTime.UtcNow
            }
        ],
        Breadcrumbs =
        [
            new BreadcrumbEntry
            {
                Type = "navigation",
                Detail = "BreadcrumbRouteMarker",
                TimestampUtc = DateTime.UtcNow
            }
        ]
    };

    // --- Preview ---

    [Fact]
    public void Modal_WhenContextLoaded_ShouldRenderPreviewToggle()
    {
        var (cut, _) = RenderModal(ThinContext());

        var preview = cut.Find(".bs-preview");
        Assert.Contains("O que será enviado", preview.TextContent);
    }

    [Fact]
    public void Modal_WhenPreviewExpanded_ShouldListContextBullets()
    {
        var (cut, _) = RenderModal(ContextWith5xx());

        // Expand the preview ("O que será enviado")
        var toggle = cut.FindAll(".bs-collapse-toggle")
            .First(b => b.TextContent.Contains("O que será enviado"));
        toggle.Click();

        var list = cut.Find(".bs-preview-list");
        Assert.Contains("Tela: /inbox", list.TextContent);
        Assert.Contains("erro(s) técnico(s)", list.TextContent);
        Assert.Contains("requisição(ões) capturada(s)", list.TextContent);
        Assert.Contains("Dados sensíveis mascarados automaticamente", list.TextContent);
    }

    // --- Severity selector default = auto-detected ---

    [Fact]
    public void Modal_WhenContextHas5xx_ShouldDefaultSeveritySelectToAutoDetectedHigh()
    {
        // 5xx → SeverityDetector.Detect returns High
        var (cut, _) = RenderModal(ContextWith5xx());

        var select = cut.Find("#bs-severity");
        Assert.Equal(BugSnapSeverity.High.ToString(), select.GetAttribute("value"));
    }

    // --- Category override chip ---

    [Fact]
    public void Modal_ShouldShowDetectedCategoryChip()
    {
        var (cut, _) = RenderModal(ContextWith5xx());

        var chip = cut.Find(".bs-chip");
        Assert.Contains("Categoria detectada", chip.TextContent);
        Assert.Contains("API", chip.TextContent);
    }

    // --- Helper text when description is empty ---

    [Fact]
    public void Modal_WhenDescriptionEmpty_ShouldShowHelper()
    {
        var (cut, _) = RenderModal(ThinContext());

        var helper = cut.Find(".bs-helper");
        Assert.Equal("Descreva o problema para habilitar o envio.", helper.TextContent.Trim());
    }

    [Fact]
    public void Modal_WhenDescriptionFilled_ShouldHideHelperAndEnableSubmit()
    {
        var (cut, _) = RenderModal(ThinContext());

        cut.Find("#bs-description").Input("Algo deu errado");

        Assert.Empty(cut.FindAll(".bs-helper"));
        var submit = cut.FindAll("button").First(b => b.TextContent.Contains("Enviar"));
        Assert.False(submit.HasAttribute("disabled"));
    }

    // --- Optional fields ---

    [Fact]
    public void Modal_WhenOptionalFieldsToggled_ShouldRevealStepsAndExpectedInputs()
    {
        var (cut, _) = RenderModal(ThinContext());

        // Optional fields are collapsed by default
        Assert.Empty(cut.FindAll("#bs-steps"));

        var toggle = cut.FindAll(".bs-collapse-toggle")
            .First(b => b.TextContent.Contains("Detalhes adicionais"));
        toggle.Click();

        Assert.NotNull(cut.Find("#bs-steps"));
        Assert.NotNull(cut.Find("#bs-expected"));
    }

    // --- Submit wires the chosen values into the report ---

    [Fact]
    public void Modal_OnSubmit_ShouldPersistAutoDetectedSeverityAndSelectedValues()
    {
        var (cut, destination) = RenderModal(ContextWith5xx());

        cut.Find("#bs-description").Input("Botão de enviar travou");

        // Expand and fill optional fields
        cut.FindAll(".bs-collapse-toggle")
            .First(b => b.TextContent.Contains("Detalhes adicionais")).Click();
        cut.Find("#bs-steps").Input("1. Cliquei em enviar 2. Nada aconteceu");
        cut.Find("#bs-expected").Input("Esperava que a mensagem fosse enviada");

        cut.FindAll("button").First(b => b.TextContent.Contains("Enviar")).Click();

        var report = destination.LastReport;
        Assert.NotNull(report);
        Assert.Equal("Botão de enviar travou", report!.Description);
        Assert.Equal(BugSnapSeverity.High.ToString(), report.AutoDetectedSeverity);
        Assert.Equal(BugSnapSeverity.High, report.Severity);
        Assert.Equal(BugSnapCategory.API, report.Category);
        Assert.Equal("1. Cliquei em enviar 2. Nada aconteceu", report.StepsToReproduce);
        Assert.Equal("Esperava que a mensagem fosse enviada", report.ExpectedOrImpact);
    }

    // --- B1 fields must appear in the sanitized preview JSON (consent contract) ---

    [Fact]
    public void Modal_WhenTechnicalDetailsExpanded_PreviewJson_ShouldContainB1Fields()
    {
        // Regression guard: CloneContext used to drop Memory/RecentConsoleErrors/Breadcrumbs,
        // so the preview ("Ver detalhes tecnicos") hid data that is actually sent. The preview
        // must reflect the real (sanitized) payload — otherwise the user consents to less than
        // what leaves the browser.
        var (cut, _) = RenderModal(ContextWithB1Fields());

        // Expand "O que sera enviado", then "Ver detalhes tecnicos".
        cut.FindAll(".bs-collapse-toggle")
            .First(b => b.TextContent.Contains("O que será enviado")).Click();
        cut.FindAll(".bs-collapse-toggle")
            .First(b => b.TextContent.Contains("Ver detalhes técnicos")).Click();

        var json = cut.Find(".bs-preview-json").TextContent;

        // camelCase property names from the serializer.
        Assert.Contains("\"memory\"", json);
        Assert.Contains("jsHeapUsedBytes", json);
        Assert.Contains("managedHeapBytes", json);

        Assert.Contains("recentConsoleErrors", json);
        Assert.Contains("ConsoleBoomMarker", json);

        Assert.Contains("breadcrumbs", json);
        Assert.Contains("BreadcrumbRouteMarker", json);
    }
}
