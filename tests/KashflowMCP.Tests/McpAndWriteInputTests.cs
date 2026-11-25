using System.Diagnostics;
using System.Text.Json;
using KashFlow;
using KashflowMCP;
using KashflowMCP.Tools;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace KashflowMCP.Tests;

public class WriteInputMapperTests
{
    [Fact]
    public void StructuredCustomer_maps_documented_fields()
    {
        var customer = WriteInputMapper.CustomerFromInput(new InsertCustomerInput
        {
            Name = " Example Ltd ",
            Code = "EX01",
            Email = "accounts@example.com",
            PaymentTerms = 14,
            Postcode = "SW1A 1AA",
            Ec = 0,
        });

        Assert.Equal("Example Ltd", customer.Name);
        Assert.Equal("EX01", customer.Code);
        Assert.Equal("accounts@example.com", customer.Email);
        Assert.Equal(14, customer.PaymentTerms);
        Assert.Equal("SW1A 1AA", customer.Postcode);
        Assert.Equal(0, customer.EC);
    }

    [Fact]
    public void LegacyCustomerJson_keeps_fields_outside_the_structured_schema()
    {
        const string json = """{"name":"Acme","code":"ACME","extraText1":"locker","paymentTerms":30}""";
        var customer = WriteInputMapper.ResolveCustomer(
            new InsertCustomerInput { Name = "Structured" },
            json);

        Assert.Equal("Acme", customer.Name);
        Assert.Equal("ACME", customer.Code);
        Assert.Equal("locker", customer.ExtraText1);
        Assert.Equal(30, customer.PaymentTerms);
    }

    [Fact]
    public void StructuredInvoice_sums_net_and_vat_and_maps_lines()
    {
        var invoice = WriteInputMapper.InvoiceFromInput(new InsertInvoiceInput
        {
            CustomerId = 5,
            CurrencyCode = "GBP",
            InvoiceDate = new DateTime(2026, 1, 15),
            Lines =
            [
                new InsertDocumentLineInput
                {
                    Quantity = 2,
                    Description = "Paper",
                    Rate = 10,
                    ChargeType = 4000,
                    VatRate = 20,
                },
            ],
        });

        Assert.Equal(5, invoice.CustomerID);
        Assert.Equal("GBP", invoice.CurrencyCode);
        Assert.Equal(20m, invoice.NetAmount);
        Assert.Equal(4m, invoice.VATAmount);
        Assert.Equal("Paper", invoice.Lines![0].Description);
        Assert.Equal(4000, invoice.Lines[0].ChargeType);
        Assert.Equal(20m, invoice.Lines[0].VatRate);
    }

    [Fact]
    public void LegacyInvoiceJson_deserializes_lines()
    {
        const string json = """
            {"customerID":3,"currencyCode":"GBP","lines":[{"quantity":1,"description":"Ink","rate":5,"chargeType":4000,"vatRate":0,"vatAmount":0}]}
            """;
        var invoice = WriteInputMapper.InvoiceFromJson(json);
        Assert.Equal(3, invoice.CustomerID);
        Assert.Equal("Ink", invoice.Lines![0].Description);
        Assert.Equal(5m, invoice.Lines[0].Rate);
    }

    [Fact]
    public void StructuredReceipt_and_journal_map_required_fields()
    {
        var receipt = WriteInputMapper.ReceiptFromInput(new InsertReceiptInput
        {
            SupplierId = 7,
            CurrencyCode = "GBP",
            ReceiptDate = new DateTime(2026, 3, 15),
            Lines =
            [
                new InsertDocumentLineInput
                {
                    Quantity = 1,
                    Description = "Clips",
                    Rate = 10.5m,
                    ChargeType = 5000,
                    VatRate = 20,
                    VatAmount = 2.1m,
                    ImportDuty = 0.25m,
                },
            ],
        });
        Assert.Equal(7, receipt.SupplierId);
        Assert.Equal(10.5m, receipt.NetAmount);
        Assert.Equal(2.1m, receipt.VATAmount);
        Assert.Equal(0.25m, receipt.Lines![0].ImportDuty);

        var journal = WriteInputMapper.JournalFromInput(new InsertJournalInput
        {
            JournalDate = new DateTime(2026, 4, 1),
            Comment = "Opening",
            Lines =
            [
                new InsertJournalLineInput { NominalId = 1000, Amount = 50m, Comment = "debit" },
                new InsertJournalLineInput { NominalId = 2000, Amount = -50m, Comment = "credit" },
            ],
        });
        Assert.Equal("Opening", journal.Comment);
        Assert.Equal(2, journal.Lines!.Count);
        Assert.Equal(1000, journal.Lines[0].NominalID);
        Assert.Equal(-50m, journal.Lines[1].Amount);
    }
}

public class KashFlowSessionTests
{
    private const string Sentinel = "sentinel-password-9f3c2a7b";

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData("${KASHFLOW_READ_ONLY}", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("yes", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("0", false)]
    [InlineData("no", false)]
    [InlineData("maybe", true)]
    public void ParseReadOnly_defaults_to_safe(string? value, bool expected)
        => Assert.Equal(expected, KashFlowSession.ParseReadOnly(value));

    [Fact]
    public void Missing_credentials_do_not_open_a_client_and_do_not_echo_a_password()
    {
        var session = KashFlowSession.FromConfiguration(Config(new Dictionary<string, string?>
        {
            ["KashFlow:Password"] = Sentinel,
        }));

        Assert.True(session.ReadOnly);
        Assert.False(session.CredentialsConfigured);
        var error = Assert.Throws<ModelContextProtocol.McpException>(() => session.Client);
        Assert.Contains("SOAP", error.Message, StringComparison.Ordinal);
        Assert.Contains("never logged", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadOnly_client_blocks_writes_without_logging_the_password()
    {
        using var session = KashFlowSession.FromConfiguration(Config(new Dictionary<string, string?>
        {
            ["KashFlow:UserName"] = "soap-user",
            ["KashFlow:Password"] = Sentinel,
            ["KashFlow:ReadOnly"] = "true",
        }));

        Assert.True(session.CredentialsConfigured);
        var error = await Assert.ThrowsAsync<KashFlowException>(() =>
            session.Client.InsertCustomerAsync(new Customer { Name = "Nope" }));
        Assert.Contains("read-only", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Sentinel, error.ToString(), StringComparison.Ordinal);
    }

    private static IConfiguration Config(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}

public class PluginManifestTests
{
    [Fact]
    public void Marketplace_files_declare_every_user_variable_and_no_parent_paths()
    {
        var root = RepoRoot();
        var plugin = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, ".cursor-plugin", "plugin.json")));
        var mcp = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "mcp.json")));

        Assert.Equal("kashflow", plugin.RootElement.GetProperty("name").GetString());
        Assert.Equal("1.1.0", plugin.RootElement.GetProperty("version").GetString());
        Assert.Equal("assets/logo.svg", plugin.RootElement.GetProperty("logo").GetString());
        Assert.False(plugin.RootElement.GetProperty("logo").GetString()!.Contains("..", StringComparison.Ordinal));

        var properties = plugin.RootElement.GetProperty("variables").GetProperty("properties");
        Assert.True(properties.TryGetProperty("KASHFLOW_USERNAME", out _));
        Assert.True(properties.TryGetProperty("KASHFLOW_PASSWORD", out var password));
        Assert.Contains("password", password.GetProperty("title").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.True(properties.TryGetProperty("KASHFLOW_READ_ONLY", out var readOnly));
        Assert.Equal("boolean", readOnly.GetProperty("type").GetString());
        Assert.True(readOnly.GetProperty("default").GetBoolean());

        var server = mcp.RootElement.GetProperty("mcpServers").GetProperty("kashflow");
        var command = server.GetProperty("command").GetString()!;
        Assert.Contains("${CURSOR_PLUGIN_ROOT}/scripts/kashflow-mcp", command, StringComparison.Ordinal);
        Assert.DoesNotContain("..", command, StringComparison.Ordinal);
        var env = server.GetProperty("env");
        Assert.Equal("${KASHFLOW_USERNAME}", env.GetProperty("KashFlow__UserName").GetString());
        Assert.Equal("${KASHFLOW_PASSWORD}", env.GetProperty("KashFlow__Password").GetString());
        Assert.Equal("${KASHFLOW_READ_ONLY}", env.GetProperty("KashFlow__ReadOnly").GetString());

        foreach (var file in new[] { ".cursor-plugin/plugin.json", "mcp.json" })
        {
            var text = File.ReadAllText(Path.Combine(root, file));
            Assert.DoesNotContain("../", text, StringComparison.Ordinal);
        }

        var version = plugin.RootElement.GetProperty("version").GetString();
        Assert.Contains($"<Version>{version}</Version>", File.ReadAllText(Path.Combine(root, "src/KashflowMCP/KashflowMCP.csproj")), StringComparison.Ordinal);
        Assert.Contains($"KASHFLOW_MCP_VERSION:-{version}", File.ReadAllText(Path.Combine(root, "scripts/kashflow-mcp")), StringComparison.Ordinal);
        Assert.Contains($"'{version}'", File.ReadAllText(Path.Combine(root, "scripts/kashflow-mcp.ps1")), StringComparison.Ordinal);
        Assert.Contains("PackageLicenseExpression>MIT", File.ReadAllText(Path.Combine(root, "src/KashflowMCP/KashflowMCP.csproj")), StringComparison.Ordinal);
        Assert.Contains("2026 Logic Ethos Ltd", File.ReadAllText(Path.Combine(root, "LICENSE")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Launcher_verifies_checksum_before_exec()
    {
        var root = RepoRoot();
        var start = new ProcessStartInfo("bash", Path.Combine(root, "scripts/test-launcher.sh"))
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("failed to start launcher test");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await process.WaitForExitAsync(timeout.Token);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        Assert.True(process.ExitCode == 0, stdout + stderr);
        Assert.Contains("launcher ok", stdout, StringComparison.Ordinal);
    }

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "KashflowConnector.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }
}

public class McpToolsListTests
{
    private const string Sentinel = "sentinel-password-9f3c2a7b";

    [Fact]
    public async Task ToolsList_works_without_credentials_and_stays_read_only()
    {
        var stderr = new List<string>();
        await using var client = await ConnectAsync(new Dictionary<string, string?>
        {
            ["KashFlow__UserName"] = null,
            ["KashFlow__Password"] = null,
            ["KashFlow__ReadOnly"] = null,
        }, stderr);

        var tools = await client.ListToolsAsync();
        var names = tools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("get_customers", names);
        Assert.Contains("get_invoice", names);
        Assert.Contains("get_bank_accounts", names);
        Assert.Contains("get_receipt_payments", names);
        Assert.Contains("get_receipt_payment_methods", names);
        Assert.Contains("search_bank_transactions", names);
        Assert.Contains("get_quotes", names);
        Assert.Contains("get_balance_sheet", names);
        Assert.Contains("get_projects", names);
        Assert.DoesNotContain("insert_customer", names);
        Assert.DoesNotContain("insert_invoice", names);
        Assert.DoesNotContain("insert_receipt_payment", names);
        Assert.DoesNotContain("allocate_bank_payment_to_receipts", names);
        Assert.DoesNotContain("insert_fx_receipt_payment", names);
        Assert.DoesNotContain("write_off_receipt_rounding", names);
        Assert.DoesNotContain("insert_invoice_payment", names);
        Assert.DoesNotContain("delete_receipt_payment", names);
        Assert.DoesNotContain("insert_bank_transaction", names);
        Assert.DoesNotContain("delete_customer", names);
        Assert.DoesNotContain("create_account", names);
        Assert.DoesNotContain("sage_file", names);
        Assert.DoesNotContain("transfer_between_bank_accounts", names);

        var joined = string.Join("\n", stderr);
        Assert.Contains("Password is never logged.", joined, StringComparison.Ordinal);
        Assert.Contains("ReadOnly=True", joined, StringComparison.Ordinal);
        Assert.Contains("credentialsConfigured=False", joined, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToolCall_without_credentials_fails_before_the_network_and_does_not_echo_a_password()
    {
        var stderr = new List<string>();
        await using var client = await ConnectAsync(new Dictionary<string, string?>
        {
            ["KashFlow__UserName"] = "",
            ["KashFlow__Password"] = Sentinel,
            ["KashFlow__ReadOnly"] = "true",
        }, stderr);

        var tool = (await client.ListToolsAsync()).Single(item => item.Name == "get_customers");
        var result = await tool.CallAsync(new Dictionary<string, object?>());
        Assert.True(result.IsError);
        var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        var logs = string.Join("\n", stderr);
        Assert.Contains("SOAP", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel, logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteTools_expose_structured_fields_and_legacy_json()
    {
        await using var client = await ConnectAsync(new Dictionary<string, string?>
        {
            ["KashFlow__UserName"] = null,
            ["KashFlow__Password"] = null,
            ["KashFlow__ReadOnly"] = "false",
        }, []);

        var tools = (await client.ListToolsAsync()).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        AssertWriteSchema(tools["insert_customer"], "customer", "customerJson", "name", "code", "email");
        AssertWriteSchema(tools["insert_supplier"], "supplier", "supplierJson", "name", "code", "vatNumber");
        AssertWriteSchema(tools["insert_invoice"], "invoice", "invoiceJson", "customerId", "lines", "currencyCode");
        AssertWriteSchema(tools["insert_receipt2"], "receipt", "receiptJson", "supplierId", "lines", "currencyCode");
        AssertWriteSchema(tools["insert_journal"], "journal", "journalJson", "journalDate", "lines", "comment");
        var receiptPayment = tools["insert_receipt_payment"].JsonSchema;
        foreach (var field in new[]
        {
            "receiptNumber", "paymentDate", "amount", "paymentMethodId", "paymentMethodName",
            "bankAccountId", "bankAccountCode", "note",
        })
        {
            Assert.True(receiptPayment.GetProperty("properties").TryGetProperty(field, out var property), field);
            Assert.True(property.TryGetProperty("description", out var fieldDescription) && fieldDescription.GetString()!.Length > 0, field);
        }

        foreach (var writeName in new[]
                 {
                     "insert_invoice_payment", "delete_invoice_payment", "delete_receipt_payment", "insert_bank_transaction",
                     "update_customer", "delete_customer", "update_invoice", "delete_invoice", "update_receipt2", "insert_quote",
                     "transfer_between_bank_accounts", "allocate_bank_payment_to_receipts", "insert_fx_receipt_payment",
                     "write_off_receipt_rounding",
                 })
            Assert.Contains(writeName, tools.Keys);
        Assert.Equal(1, tools.Keys.Count(name => name == "insert_bank_transaction"));
        Assert.True(tools["insert_invoice_payment"].JsonSchema.GetProperty("properties").TryGetProperty("invoicePayment", out _));
        Assert.True(tools["delete_receipt_payment"].JsonSchema.GetProperty("properties").TryGetProperty("receiptPaymentNumber", out _));
        Assert.True(tools["insert_bank_transaction"].JsonSchema.GetProperty("properties").TryGetProperty("bp", out _));
        var transfer = tools["transfer_between_bank_accounts"].JsonSchema.GetProperty("properties");
        foreach (var field in new[]
        {
            "amount", "transferDate", "fromBankAccountId", "fromBankAccountCode",
            "toBankAccountId", "toBankAccountCode", "toNominalId", "comment",
        })
        {
            Assert.True(transfer.TryGetProperty(field, out var transferProperty), field);
            Assert.True(transferProperty.TryGetProperty("description", out var transferDescription) && transferDescription.GetString()!.Length > 0, field);
        }
        Assert.False(transfer.TryGetProperty("transactionTypeId", out _), "transactionTypeId was removed; destination uses toNominalId");

        var allocate = tools["allocate_bank_payment_to_receipts"].JsonSchema.GetProperty("properties");
        foreach (var field in new[]
                 {
                     "paymentDate", "totalAmount", "allocations", "bankAccountId", "bankAccountCode",
                     "paymentMethodId", "paymentMethodName", "note",
                 })
            Assert.True(allocate.TryGetProperty(field, out _), field);

        var fx = tools["insert_fx_receipt_payment"].JsonSchema.GetProperty("properties");
        foreach (var field in new[]
                 {
                     "receiptNumber", "paymentDate", "bankAmountGbp", "bankAccountId", "bankAccountCode",
                     "paymentMethodId", "paymentMethodName", "note",
                 })
            Assert.True(fx.TryGetProperty(field, out _), field);

        var writeOff = tools["write_off_receipt_rounding"].JsonSchema.GetProperty("properties");
        foreach (var field in new[] { "receiptNumber", "paymentDate", "nominal", "maxAmount", "note" })
            Assert.True(writeOff.TryGetProperty(field, out _), field);

        Assert.Contains("search_bank_transactions", tools.Keys);
        var search = tools["search_bank_transactions"].JsonSchema.GetProperty("properties");
        foreach (var field in new[]
                 {
                     "amount", "tolerance", "direction", "dateFrom", "dateTo", "bankAccountIds", "bankAccountCodes",
                 })
            Assert.True(search.TryGetProperty(field, out _), field);

        var customer = ResolveSchema(tools["insert_customer"].JsonSchema, Property(tools["insert_customer"].JsonSchema, "properties", "customer"));
        var name = ResolveSchema(tools["insert_customer"].JsonSchema, Property(customer, "properties", "name"));
        Assert.True(name.TryGetProperty("description", out var description));
        Assert.Contains("name", description.GetString(), StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertWriteSchema(McpClientTool tool, string structuredName, string legacyName, params string[] fields)
    {
        var root = tool.JsonSchema;
        Assert.True(root.TryGetProperty("properties", out _), tool.JsonSchema.ToString());
        var structured = ResolveSchema(root, Property(root, "properties", structuredName));
        var legacy = ResolveSchema(root, Property(root, "properties", legacyName));
        Assert.True(HasType(legacy, "string"), legacy.ToString());
        foreach (var field in fields)
        {
            Assert.True(
                structured.GetProperty("properties").TryGetProperty(field, out var property),
                $"missing {structuredName}.{field} in {root}");
            var described = property.TryGetProperty("description", out var description)
                ? description
                : ResolveSchema(root, property).TryGetProperty("description", out description)
                    ? description
                    : default;
            Assert.True(described.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(described.GetString()),
                $"{structuredName}.{field} has no description: {property}");
        }
    }

    private static bool HasType(JsonElement schema, string typeName)
    {
        if (!schema.TryGetProperty("type", out var type))
            return false;
        if (type.ValueKind == JsonValueKind.String)
            return type.GetString() == typeName;
        if (type.ValueKind == JsonValueKind.Array)
            return type.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && item.GetString() == typeName);
        return false;
    }

    private static JsonElement Property(JsonElement element, string objectName, string propertyName)
    {
        if (!element.TryGetProperty(objectName, out var obj) || !obj.TryGetProperty(propertyName, out var property))
            throw new Xunit.Sdk.XunitException($"missing {objectName}.{propertyName} in {element}");
        return property;
    }

    private static JsonElement ResolveSchema(JsonElement root, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("$ref", out var reference))
        {
            var pointer = reference.GetString() ?? "";
            var cursor = root;
            foreach (var part in pointer.TrimStart('#').Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                var name = Uri.UnescapeDataString(part);
                if (!cursor.TryGetProperty(name, out cursor))
                    throw new Xunit.Sdk.XunitException($"could not resolve {pointer} in {root}");
            }

            return cursor;
        }

        return element;
    }

    private static async Task<McpClient> ConnectAsync(Dictionary<string, string?> environment, List<string> stderr)
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "KashflowMCP.dll");
        Assert.True(File.Exists(dll), $"missing {dll}");
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "kashflow-test",
            Command = "dotnet",
            Arguments = ["exec", dll],
            WorkingDirectory = AppContext.BaseDirectory,
            EnvironmentVariables = environment,
            StandardErrorLines = line =>
            {
                lock (stderr)
                    stderr.Add(line);
            },
        });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        return await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);
    }
}
