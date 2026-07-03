using System.Net;
using NbTcgTrader.Api.Features.Import;
using Shouldly;

namespace NbTcgTrader.Tests;

// Coverage for the downloadable CSV import template (BACKLOG #13). The endpoint
// serves static content and never touches the database, so it runs on the shared
// no-database host. Asserts the over-the-wire contract: anonymous access, a CSV
// attachment, and exactly the CLAUDE.md §9 header row.
[Collection(IntegrationTestCollection.Name)]
public sealed class ImportTemplateTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public ImportTemplateTests(ApiWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Template_downloads_anonymously_as_a_csv_attachment()
    {
        var client = _factory.CreateClient();

        // No Authorization header: the template must be reachable before login.
        var response = await client.GetAsync("/import/template");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/csv");
        response.Content.Headers.ContentDisposition.ShouldNotBeNull();
        response.Content.Headers.ContentDisposition.DispositionType.ShouldBe("attachment");
        response.Content.Headers.ContentDisposition.FileNameStar
            .ShouldBe("nb-tcg-import-template.csv");
    }

    [Fact]
    public async Task Template_body_is_exactly_the_contract_header_row()
    {
        var client = _factory.CreateClient();

        var body = await client.GetStringAsync("/import/template");

        // Pin the literal wire format (column order, no spaces, CRLF-terminated):
        // the upload parser (#14) and any user spreadsheet build on this line.
        body.ShouldBe("card_name,set,card_number,quantity,condition,price,for_sale\r\n");
    }

    [Fact]
    public void Template_cells_need_no_csv_injection_escaping()
    {
        // Guards the CLAUDE.md §15 rule at the contract level: if a column is ever
        // renamed to start with = + - @, this fails before a spreadsheet can run it.
        foreach (var column in ImportTemplate.Header)
        {
            column.ShouldNotBeNullOrWhiteSpace();
            "=+-@".ShouldNotContain(column[0]);
        }
    }
}
