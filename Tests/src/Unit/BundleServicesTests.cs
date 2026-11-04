namespace BlueHeighliner.Bundler.Tests;

public sealed class BundleServicesTests
{
    private static BundleServices Create(string text)
    {
        Mock<IResources> resources = new();
        resources.Setup(r => r.Text("Bundles.md")).Returns(text);
        return new(resources.Object);
    }

    [Fact]
    public void For_ReadsTheServicesTableOfTheSection()
    {
        BundleServices services = Create("""
            # Bundles

            | Bundle | Needs | Installs |
            |---|---|---|
            | [`git`](#git) | none | git |

            ## git

            Text.

            | Service | Version | Description | License | Usage |
            |---|---|---|---|---|
            | git | 2.56.0 | Source control | GPL-2.0, MIT | Clear. See [notes](x.md). |
            | `curl` | - | A \| B | MIT | Clear. |

            ## dev

            | Service | Version | Description | License | Usage |
            |---|---|---|---|---|
            | .NET SDK | 10 | Builds | MIT | Clear. |
            """);

        IReadOnlyList<ServiceRow> rows = services.For("git");

        Assert.Equal(2, rows.Count);
        Assert.Equal("git", rows[0].Service);
        Assert.Equal("2.56.0", rows[0].Version);
        Assert.Equal(["GPL-2.0", "MIT"], rows[0].Licenses);
        Assert.Equal("Clear. See notes.", rows[0].Usage);
        Assert.Equal(["GPL-2.0", "MIT"], rows[0].Links.Select(link => link.Id));
        Assert.All(rows[0].Links, link => Assert.Equal(["GPL-2.0", "MIT"], link.All));
        Assert.Equal("A | B", rows[1].Description);
        Assert.Equal("curl", rows[1].Service);
        Assert.Equal(".NET SDK", Assert.Single(services.For("dev")).Service);
    }

    [Fact]
    public void For_UnknownBundle_IsEmpty()
        => Assert.Empty(Create("## git\n").For("nothing"));

    [Fact]
    public void BundlesMd_BuiltIntoTheExecutable_HasServicesForEveryBundle()
    {
        BundleServices services = new(new Resources());
        using ServiceProvider provider = new ServiceCollection().AddBundler(_ => new ObservableLog()).BuildServiceProvider();

        foreach (IBundle bundle in provider.GetRequiredService<IBundleCatalog>().All)
        {
            Assert.NotEmpty(services.For(bundle.Name));
        }
    }

    [Fact]
    public void EveryLicenseNamedInBundlesMd_HasAFullText()
    {
        BundleServices services = new(new Resources());
        LicenseTexts texts = new(new Resources());
        using ServiceProvider provider = new ServiceCollection().AddBundler(_ => new ObservableLog()).BuildServiceProvider();

        string[] licenses = provider.GetRequiredService<IBundleCatalog>().All.SelectMany(bundle => services.For(bundle.Name)).SelectMany(row => row.Licenses).Distinct().ToArray();

        Assert.NotEmpty(licenses);
        Assert.All(licenses, license => Assert.DoesNotContain("is not included", texts.Text(license)));
    }
}
