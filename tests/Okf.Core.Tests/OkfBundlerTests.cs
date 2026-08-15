using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Okf.Core;

namespace Okf.Core.Tests;

/// <summary>
/// The bundler (work item #5): packaging a vault's bundles for consume-only distribution.
/// </summary>
/// <remarks>
/// Expectations here come from the requirement, not from the implementation: the file set
/// is spelled out literally, the determinism claim is checked by building the same archive
/// twice a second apart and comparing bytes, and the dangling-link claim is checked by
/// packaging a subset of a vault whose bundles link to each other.
/// </remarks>
public class OkfBundlerTests
{
    /// <summary>A fixed stamp, so a test never depends on the clock.</summary>
    private static readonly DateTimeOffset Stamp = new(2026, 8, 15, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Packages_bundle_content_and_nothing_from_the_vault_around_it()
    {
        using var tree = new BundlerVault();

        var plan = OkfBundler.Plan(tree.WorkingSet(), Options());

        Assert.Equal(
            [
                "bundles/alpha/about-this-bundle.md",
                "bundles/alpha/index.md",
                "bundles/alpha/log.md",
                "bundles/alpha/references/attester.py",
                "bundles/alpha/topics/about.md",
                "bundles/alpha/topics/index.md",
                "bundles/alpha/topics/widgets.md",
                "bundles/beta/gadgets.md",
                "bundles/beta/index.md",
            ],
            plan.Entries.Select(entry => entry.Path).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Records_a_sha256_for_every_packaged_file_and_none_for_the_manifest()
    {
        using var tree = new BundlerVault();

        var plan = OkfBundler.Plan(tree.WorkingSet(), Options());

        Assert.Equal(plan.Entries.Count, plan.Manifest.Files.Count);
        Assert.DoesNotContain(
            plan.Manifest.Files,
            file => file.Path == OkfDistributionManifest.FileName);

        var index = plan.Manifest.Files.Single(file => file.Path == "bundles/beta/gadgets.md");
        Assert.Equal(
            OkfCaptureManifest.Sha256Of(Path.Combine(tree.Root, "bundles", "beta", "gadgets.md")),
            index.Sha256);
    }

    [Fact]
    public void Manifest_names_the_format_the_generator_the_vault_and_the_bundles()
    {
        using var tree = new BundlerVault();

        var manifest = OkfBundler.Plan(tree.WorkingSet(), Options()).Manifest;

        Assert.Equal(1, manifest.ManifestVersion);
        Assert.Equal("0.2", manifest.OkfVersion);
        Assert.Equal("okf/9.9.9-test", manifest.Generator);
        Assert.Equal(tree.VaultName, manifest.SourceVault);
        Assert.Equal("2026-08-15T14:00:00Z", manifest.GeneratedAt);
        Assert.Equal(["alpha", "beta"], manifest.Bundles);
    }

    [Fact]
    public void Manifest_round_trips_through_its_own_json()
    {
        using var tree = new BundlerVault();
        var manifest = OkfBundler.Plan(tree.WorkingSet(), Options(bundles: ["alpha"])).Manifest;

        var parsed = OkfDistributionManifest.Parse(manifest.ToJson());

        Assert.NotNull(parsed);
        Assert.Equal(manifest.ManifestVersion, parsed.ManifestVersion);
        Assert.Equal(manifest.OkfVersion, parsed.OkfVersion);
        Assert.Equal(manifest.Generator, parsed.Generator);
        Assert.Equal(manifest.SourceVault, parsed.SourceVault);
        Assert.Equal(manifest.GeneratedAt, parsed.GeneratedAt);
        Assert.Equal(manifest.Bundles, parsed.Bundles);
        Assert.Equal(
            manifest.Files.Select(file => (file.Path, file.Sha256)),
            parsed.Files.Select(file => (file.Path, file.Sha256)));
        Assert.Equal(
            manifest.ExternalLinks.Select(link => (link.From, link.To, link.Bundle)),
            parsed.ExternalLinks.Select(link => (link.From, link.To, link.Bundle)));
    }

    [Fact]
    public void A_subset_records_the_links_that_now_leave_the_distribution()
    {
        using var tree = new BundlerVault();

        var manifest = OkfBundler.Plan(tree.WorkingSet(), Options(bundles: ["alpha"])).Manifest;

        var link = Assert.Single(manifest.ExternalLinks);
        Assert.Equal("bundles/alpha/topics/widgets.md", link.From);
        Assert.Equal("../../beta/gadgets.md", link.To);
        Assert.Equal("beta", link.Bundle);
    }

    [Fact]
    public void The_whole_vault_records_no_dangling_link_because_the_target_ships_too()
    {
        using var tree = new BundlerVault();

        var manifest = OkfBundler.Plan(tree.WorkingSet(), Options()).Manifest;

        Assert.Empty(manifest.ExternalLinks);
    }

    [Fact]
    public void A_link_into_the_stripped_vault_is_recorded_with_no_bundle()
    {
        using var tree = new BundlerVault();
        tree.Write(
            "bundles/beta/gadgets.md",
            Concept("Gadgets", "See [the capture](../../raw/2026-08-15-thing.html)."));

        var manifest = OkfBundler.Plan(tree.WorkingSet(), Options()).Manifest;

        var link = Assert.Single(manifest.ExternalLinks);
        Assert.Equal("bundles/beta/gadgets.md", link.From);
        Assert.Null(link.Bundle);
    }

    [Fact]
    public void A_link_to_a_packaged_bundles_directory_is_not_dangling()
    {
        using var tree = new BundlerVault();
        tree.Write(
            "bundles/alpha/topics/widgets.md",
            Concept("Widgets", "Everything about [gadgets](../../beta/) lives next door."));

        var manifest = OkfBundler.Plan(tree.WorkingSet(), Options()).Manifest;

        Assert.Empty(manifest.ExternalLinks);
    }

    [Fact]
    public void A_link_to_a_bundle_left_out_names_that_bundle_even_as_a_bare_directory()
    {
        using var tree = new BundlerVault();
        tree.Write(
            "bundles/alpha/topics/widgets.md",
            Concept("Widgets", "Everything about [gadgets](../../beta/) lives next door."));

        var manifest = OkfBundler.Plan(tree.WorkingSet(), Options(bundles: ["alpha"])).Manifest;

        var link = Assert.Single(manifest.ExternalLinks);
        Assert.Equal("../../beta/", link.To);
        Assert.Equal("beta", link.Bundle);
    }

    [Fact]
    public void Dangling_links_are_deduplicated_and_ordered_by_document_then_line()
    {
        using var tree = new BundlerVault();
        tree.Write(
            "bundles/alpha/about-this-bundle.md",
            Concept("About Alpha", "See [gadgets](../beta/gadgets.md)."));
        tree.Write(
            "bundles/alpha/topics/widgets.md",
            Concept(
                "Widgets",
                "First [zeta](../../beta/zeta.md),\nthen [gadgets](../../beta/gadgets.md),\n"
                + "then [zeta again](../../beta/zeta.md),\n"
                // Two on one line, so the tie-break past the line number is exercised.
                + "and finally [psi](../../beta/psi.md) beside [omega](../../beta/omega.md)."));

        var manifest = OkfBundler.Plan(tree.WorkingSet(), Options(bundles: ["alpha"])).Manifest;

        Assert.Equal(
            [
                ("bundles/alpha/about-this-bundle.md", "../beta/gadgets.md"),
                ("bundles/alpha/topics/widgets.md", "../../beta/zeta.md"),
                ("bundles/alpha/topics/widgets.md", "../../beta/gadgets.md"),
                ("bundles/alpha/topics/widgets.md", "../../beta/omega.md"),
                ("bundles/alpha/topics/widgets.md", "../../beta/psi.md"),
            ],
            manifest.ExternalLinks.Select(link => (link.From, link.To)));
    }

    /// <summary>
    /// A link that dangles *inside* its own bundle is an ordinary §6.1 broken link — the
    /// linter's business (OKF0302) and not the distribution's, because packaging neither
    /// caused it nor can fix it. Only links that leave the bundle are recorded.
    /// </summary>
    [Fact]
    public void A_link_broken_inside_its_own_bundle_is_not_a_distribution_finding()
    {
        using var tree = new BundlerVault();
        tree.Write(
            "bundles/beta/gadgets.md",
            Concept("Gadgets", "See [the missing one](not-written-yet.md)."));

        var manifest = OkfBundler.Plan(tree.WorkingSet(), Options()).Manifest;

        Assert.Empty(manifest.ExternalLinks);
    }

    [Fact]
    public void The_entries_and_the_manifest_agree_on_one_ordinal_order()
    {
        using var tree = new BundlerVault();

        var plan = OkfBundler.Plan(tree.WorkingSet(), Options());

        var paths = plan.Entries.Select(entry => entry.Path).ToList();
        Assert.Equal(paths.Order(StringComparer.Ordinal), paths);
        Assert.Equal(paths, plan.Manifest.Files.Select(file => file.Path));
    }

    [Fact]
    public void The_packaged_bundles_are_ordered_by_name_whatever_order_they_were_asked_for()
    {
        using var tree = new BundlerVault();

        var manifest = OkfBundler.Plan(tree.WorkingSet(), Options(bundles: ["beta", "alpha"])).Manifest;

        Assert.Equal(["alpha", "beta"], manifest.Bundles);
    }

    [Fact]
    public void The_total_is_the_sum_of_the_packaged_files_not_the_largest_of_them()
    {
        using var tree = new BundlerVault();

        var plan = OkfBundler.Plan(tree.WorkingSet(), Options());

        Assert.Equal(
            plan.Entries.Select(entry => new FileInfo(entry.SourcePath).Length).Sum(),
            plan.TotalBytes);
        Assert.True(plan.TotalBytes > plan.Entries.Max(entry => entry.Length));
    }

    [Fact]
    public void A_bundle_outside_any_vault_packages_with_no_source_vault()
    {
        using var tree = new BundlerVault();
        var lone = tree.CreateDirectory("foreign/handbook");
        File.WriteAllText(Path.Combine(lone, "gadgets.md"), Concept("Gadgets", "Gadgets."));
        var workingSet = OkfDiscovery.Resolve(lone, new OkfEnvironment(tree.Output));

        var plan = OkfBundler.Plan(workingSet, Options());

        Assert.Null(plan.Manifest.SourceVault);
        Assert.Contains("\"sourceVault\": null", plan.Manifest.ToJson(), StringComparison.Ordinal);
        Assert.Equal(["bundles/handbook/gadgets.md"], plan.Entries.Select(entry => entry.Path));
    }

    [Fact]
    public void The_manifest_is_indented_and_newline_terminated_because_a_consumer_reads_it()
    {
        using var tree = new BundlerVault();

        var json = OkfBundler.Plan(tree.WorkingSet(), Options()).Manifest.ToJson();

        Assert.Contains("\n  \"okfVersion\": \"0.2\"", json, StringComparison.Ordinal);
        Assert.Contains("\"manifestVersion\": 1", json, StringComparison.Ordinal);
        Assert.EndsWith("}\n", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// The reader is tolerant in the same direction the format is: a manifest that omits
    /// what this build would have written still reads, at the versions this build knows.
    /// </summary>
    [Fact]
    public void A_manifest_missing_its_versions_reads_at_the_current_ones()
    {
        var parsed = OkfDistributionManifest.Parse(
            """{ "generator": "okf/1.0.0", "generatedAt": "2026-08-15T14:00:00Z", "files": [] }""");

        Assert.NotNull(parsed);
        Assert.Equal(OkfDistributionManifest.CurrentVersion, parsed.ManifestVersion);
        Assert.Equal(OkfDistributionManifest.SpecVersion, parsed.OkfVersion);
        Assert.Null(parsed.SourceVault);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{ "generator": "okf/1.0.0" }""")]
    [InlineData("""{ "generatedAt": "2026-08-15T14:00:00Z" }""")]
    [InlineData("[]")]
    public void A_manifest_that_is_not_one_does_not_parse(string json) =>
        Assert.Null(OkfDistributionManifest.Parse(json));

    [Fact]
    public void Verify_reports_a_file_that_is_not_an_archive_rather_than_throwing()
    {
        using var tree = new BundlerVault();
        var impostor = Path.Combine(tree.Output, "not-an-archive.tar.gz");
        File.WriteAllText(impostor, "PKZ this is a 404 page, not an archive\n");

        var result = OkfBundler.Verify(impostor);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(OkfDistributionIssue.Unreadable, finding.Issue);
    }

    [Fact]
    public void An_unknown_bundle_name_is_refused_and_the_available_names_are_listed()
    {
        using var tree = new BundlerVault();

        var failure = Assert.Throws<OkfDiscoveryException>(
            () => OkfBundler.Plan(tree.WorkingSet(), Options(bundles: ["gamma"])));

        Assert.Contains("gamma", failure.Message, StringComparison.Ordinal);
        Assert.Contains("alpha", failure.Message, StringComparison.Ordinal);
        Assert.Contains("beta", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(OkfDistributionFormat.TarGz)]
    [InlineData(OkfDistributionFormat.Zip)]
    public void The_same_vault_produces_a_byte_identical_archive(OkfDistributionFormat format)
    {
        using var tree = new BundlerVault();
        var first = Path.Combine(tree.Output, "first");
        var second = Path.Combine(tree.Output, "second");

        OkfBundler.Write(OkfBundler.Plan(tree.WorkingSet(), Options()), first, format);

        // A second later, so anything reading the clock — a gzip header stamp, a
        // per-write timestamp — shows up as a difference rather than passing by being
        // fast; and with every source file re-stamped, which is what a fresh checkout of
        // the same vault on a release runner looks like.
        Thread.Sleep(1100);
        tree.Touch(new DateTime(2024, 3, 4, 5, 6, 7, DateTimeKind.Utc));
        OkfBundler.Write(OkfBundler.Plan(tree.WorkingSet(), Options()), second, format);

        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
    }

    [Fact]
    public void Archive_entries_carry_a_fixed_timestamp_rather_than_the_clock()
    {
        using var tree = new BundlerVault();
        var archive = Path.Combine(tree.Output, "bundle.tar.gz");
        tree.Touch(new DateTime(2024, 3, 4, 5, 6, 7, DateTimeKind.Utc));

        OkfBundler.Write(OkfBundler.Plan(tree.WorkingSet(), Options()), archive, OkfDistributionFormat.TarGz);

        using var file = File.OpenRead(archive);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new TarReader(gzip);
        var stamps = new List<DateTimeOffset>();
        while (reader.GetNextEntry() is { } entry)
        {
            stamps.Add(entry.ModificationTime);
        }

        // The literal instant the decision names, not the constant the code reads: an
        // entry stamped from the clock or from the file's own mtime fails this.
        var expected = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.NotEmpty(stamps);
        Assert.All(stamps, stamp => Assert.Equal(expected, stamp));
    }

    [Fact]
    public void Archive_entries_are_written_in_ordinal_path_order_with_the_manifest_last()
    {
        using var tree = new BundlerVault();
        var archive = Path.Combine(tree.Output, "bundle.tar.gz");

        OkfBundler.Write(OkfBundler.Plan(tree.WorkingSet(), Options()), archive, OkfDistributionFormat.TarGz);

        var names = TarEntries(archive).Select(entry => entry.Name).ToList();
        Assert.Equal(names.Order(StringComparer.Ordinal), names);
        Assert.Equal(OkfDistributionManifest.FileName, names[^1]);
    }

    [Fact]
    public void Archive_entries_carry_no_owner_and_a_normalized_mode()
    {
        using var tree = new BundlerVault();
        var archive = Path.Combine(tree.Output, "bundle.tar.gz");

        OkfBundler.Write(OkfBundler.Plan(tree.WorkingSet(), Options()), archive, OkfDistributionFormat.TarGz);

        foreach (var entry in TarEntries(archive).Cast<PaxTarEntry>())
        {
            // Whose machine built the archive is not part of what was packaged, and a
            // producer's umask must not travel with the knowledge.
            Assert.Equal(string.Empty, entry.UserName);
            Assert.Equal(string.Empty, entry.GroupName);
            Assert.Equal(0, entry.Uid);
            Assert.Equal(0, entry.Gid);
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
                entry.Mode);
        }
    }

    [Fact]
    public void Tar_and_zip_carry_the_same_entries_with_the_same_bytes()
    {
        using var tree = new BundlerVault();
        var plan = OkfBundler.Plan(tree.WorkingSet(), Options());
        var tarball = Path.Combine(tree.Output, "bundle.tar.gz");
        var zipped = Path.Combine(tree.Output, "bundle.zip");

        OkfBundler.Write(plan, tarball, OkfDistributionFormat.TarGz);
        OkfBundler.Write(plan, zipped, OkfDistributionFormat.Zip);

        var fromTar = TarContents(tarball);
        var fromZip = ZipContents(zipped);
        Assert.Equal(fromTar.Keys.Order(StringComparer.Ordinal), fromZip.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, content) in fromTar)
        {
            Assert.Equal(content, fromZip[path]);
        }
    }

    [Fact]
    public void The_archive_carries_the_manifest_at_its_root()
    {
        using var tree = new BundlerVault();
        var archive = Path.Combine(tree.Output, "bundle.tar.gz");

        OkfBundler.Write(OkfBundler.Plan(tree.WorkingSet(), Options()), archive, OkfDistributionFormat.TarGz);

        var contents = TarContents(archive);
        Assert.Contains(OkfDistributionManifest.FileName, contents.Keys);
        using var document = JsonDocument.Parse(contents[OkfDistributionManifest.FileName]);
        Assert.Equal("0.2", document.RootElement.GetProperty("okfVersion").GetString());
    }

    [Fact]
    public void A_directory_distribution_holds_the_same_relative_paths()
    {
        using var tree = new BundlerVault();
        var directory = Path.Combine(tree.Output, "dist");
        var plan = OkfBundler.Plan(tree.WorkingSet(), Options());

        OkfBundler.Write(plan, directory, OkfDistributionFormat.Directory);

        var written = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(directory, file).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.Equal(
            plan.Entries.Select(entry => entry.Path).Append(OkfDistributionManifest.FileName).Order(StringComparer.Ordinal),
            written);
    }

    [Fact]
    public void A_directory_distribution_is_rewritten_in_place_leaving_nothing_stale_behind()
    {
        using var tree = new BundlerVault();
        var directory = Path.Combine(tree.Output, "dist");
        OkfBundler.Write(OkfBundler.Plan(tree.WorkingSet(), Options()), directory, OkfDistributionFormat.Directory);

        OkfBundler.Write(
            OkfBundler.Plan(tree.WorkingSet(), Options(bundles: ["beta"])),
            directory,
            OkfDistributionFormat.Directory);

        Assert.False(Directory.Exists(Path.Combine(directory, "bundles", "alpha")));
        Assert.True(File.Exists(Path.Combine(directory, "bundles", "beta", "gadgets.md")));
        Assert.True(OkfBundler.Verify(directory).IsValid);
    }

    [Fact]
    public void A_directory_holding_something_else_is_refused()
    {
        using var tree = new BundlerVault();
        var directory = tree.CreateDirectory("output/occupied");
        File.WriteAllText(Path.Combine(directory, "notes.txt"), "mine");

        var failure = Assert.Throws<IOException>(() => OkfBundler.Write(
            OkfBundler.Plan(tree.WorkingSet(), Options()),
            directory,
            OkfDistributionFormat.Directory));

        Assert.Contains(OkfDistributionManifest.FileName, failure.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(directory, "notes.txt")));
    }

    [Theory]
    [InlineData(OkfDistributionFormat.TarGz, "bundle.tar.gz")]
    [InlineData(OkfDistributionFormat.Zip, "bundle.zip")]
    [InlineData(OkfDistributionFormat.Directory, "dist")]
    public void Verify_accepts_what_the_bundler_just_wrote(OkfDistributionFormat format, string name)
    {
        using var tree = new BundlerVault();
        var output = Path.Combine(tree.Output, name);
        var plan = OkfBundler.Plan(tree.WorkingSet(), Options());
        OkfBundler.Write(plan, output, format);

        var result = OkfBundler.Verify(output);

        Assert.Empty(result.Findings);
        Assert.True(result.IsValid);
        Assert.Equal(plan.Entries.Count, result.Checked);
    }

    [Theory]
    [InlineData(OkfDistributionFormat.TarGz, "bundle.tar.gz")]
    [InlineData(OkfDistributionFormat.Zip, "bundle.zip")]
    [InlineData(OkfDistributionFormat.Directory, "dist")]
    public void Verify_reports_a_file_whose_bytes_changed(OkfDistributionFormat format, string name)
    {
        using var tree = new BundlerVault();
        var output = Path.Combine(tree.Output, name);
        OkfBundler.Write(OkfBundler.Plan(tree.WorkingSet(), Options()), output, format);
        Tamper(output, format, "bundles/beta/gadgets.md", "---\ntype: Concept\n---\n\nreplaced\n");

        var result = OkfBundler.Verify(output);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(OkfDistributionIssue.Modified, finding.Issue);
        Assert.Equal("bundles/beta/gadgets.md", finding.Path);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Verify_reports_a_file_the_manifest_never_listed()
    {
        using var tree = new BundlerVault();
        var directory = Path.Combine(tree.Output, "dist");
        OkfBundler.Write(OkfBundler.Plan(tree.WorkingSet(), Options()), directory, OkfDistributionFormat.Directory);
        File.WriteAllText(Path.Combine(directory, "bundles", "beta", "smuggled.md"), "---\ntype: Concept\n---\n");

        var result = OkfBundler.Verify(directory);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(OkfDistributionIssue.Unlisted, finding.Issue);
        Assert.Equal("bundles/beta/smuggled.md", finding.Path);
    }

    [Fact]
    public void Verify_lists_every_unlisted_file_in_path_order()
    {
        using var tree = new BundlerVault();
        var directory = Path.Combine(tree.Output, "dist");
        OkfBundler.Write(OkfBundler.Plan(tree.WorkingSet(), Options()), directory, OkfDistributionFormat.Directory);
        File.WriteAllText(Path.Combine(directory, "bundles", "beta", "zeta.md"), "---\ntype: Concept\n---\n");
        File.WriteAllText(Path.Combine(directory, "bundles", "beta", "alpha.md"), "---\ntype: Concept\n---\n");

        var result = OkfBundler.Verify(directory);

        Assert.Equal(
            ["bundles/beta/alpha.md", "bundles/beta/zeta.md"],
            result.Findings.Select(finding => finding.Path));
    }

    [Fact]
    public void Verify_reports_a_file_the_manifest_lists_and_the_distribution_lost()
    {
        using var tree = new BundlerVault();
        var directory = Path.Combine(tree.Output, "dist");
        OkfBundler.Write(OkfBundler.Plan(tree.WorkingSet(), Options()), directory, OkfDistributionFormat.Directory);
        File.Delete(Path.Combine(directory, "bundles", "beta", "gadgets.md"));

        var result = OkfBundler.Verify(directory);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(OkfDistributionIssue.Missing, finding.Issue);
        Assert.Equal("bundles/beta/gadgets.md", finding.Path);
    }

    [Fact]
    public void Verify_refuses_a_directory_with_no_manifest_rather_than_passing_it()
    {
        using var tree = new BundlerVault();

        var result = OkfBundler.Verify(tree.CreateDirectory("output/empty"));

        var finding = Assert.Single(result.Findings);
        Assert.Equal(OkfDistributionIssue.Unreadable, finding.Issue);
        Assert.False(result.IsValid);
    }

    /// <summary>The options every test uses, with the clock and the version pinned.</summary>
    private static OkfBundlerOptions Options(IReadOnlyList<string>? bundles = null) => new()
    {
        Bundles = bundles ?? [],
        GeneratedAt = Stamp,
        Generator = "okf/9.9.9-test",
    };

    private static string Concept(string title, string body) =>
        $"---\ntype: Concept\ntitle: {title}\ndescription: A fixture concept.\n---\n\n{body}\n";

    /// <summary>Every entry of a tar.gz, in the order it was written.</summary>
    private static List<TarEntry> TarEntries(string archive)
    {
        var entries = new List<TarEntry>();
        using var file = File.OpenRead(archive);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new TarReader(gzip);
        while (reader.GetNextEntry(copyData: true) is { } entry)
        {
            entries.Add(entry);
        }

        return entries;
    }

    private static Dictionary<string, string> TarContents(string archive)
    {
        var contents = new Dictionary<string, string>(StringComparer.Ordinal);
        using var file = File.OpenRead(archive);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new TarReader(gzip);
        while (reader.GetNextEntry() is { } entry)
        {
            using var data = new MemoryStream();
            entry.DataStream!.CopyTo(data);
            contents[entry.Name] = Encoding.UTF8.GetString(data.ToArray());
        }

        return contents;
    }

    private static Dictionary<string, string> ZipContents(string archive)
    {
        var contents = new Dictionary<string, string>(StringComparer.Ordinal);
        using var zip = ZipFile.OpenRead(archive);
        foreach (var entry in zip.Entries)
        {
            using var stream = entry.Open();
            using var data = new MemoryStream();
            stream.CopyTo(data);
            contents[entry.FullName] = Encoding.UTF8.GetString(data.ToArray());
        }

        return contents;
    }

    /// <summary>Replaces one file's content inside a written distribution, whatever its shape.</summary>
    private static void Tamper(string output, OkfDistributionFormat format, string path, string content)
    {
        switch (format)
        {
            case OkfDistributionFormat.Directory:
                File.WriteAllText(Path.Combine(output, path.Replace('/', Path.DirectorySeparatorChar)), content);
                return;

            case OkfDistributionFormat.Zip:
            {
                using var zip = ZipFile.Open(output, ZipArchiveMode.Update);
                var entry = zip.GetEntry(path)!;
                using var stream = entry.Open();
                stream.SetLength(0);
                stream.Write(Encoding.UTF8.GetBytes(content));
                return;
            }

            default:
            {
                var contents = TarContents(output);
                contents[path] = content;
                using var file = File.Create(output);
                using var gzip = new GZipStream(file, CompressionLevel.Optimal);
                using var writer = new TarWriter(gzip, TarEntryFormat.Pax);
                foreach (var (name, text) in contents.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name)
                    {
                        DataStream = new MemoryStream(Encoding.UTF8.GetBytes(text)),
                    });
                }

                return;
            }
        }
    }

    /// <summary>
    /// A throwaway vault in the layout decisions.md §2 fixes: two bundles, the custodian
    /// machinery beside them, and the junk a real working tree accumulates.
    /// </summary>
    private sealed class BundlerVault : IDisposable
    {
        private readonly string parent;

        public BundlerVault()
        {
            this.parent = Path.Combine(Path.GetTempPath(), "okf-tests", Path.GetRandomFileName());
            Root = Path.Combine(this.parent, "vault-project", OkfDiscovery.VaultDirectoryName);
            Output = Path.Combine(this.parent, "out");
            Directory.CreateDirectory(Output);

            // The bundles: concepts, reserved files, an about file, and a non-markdown
            // attester, which is bundle content the spec's own examples carry (§6.3).
            Write("bundles/alpha/index.md", "<!-- generated by okf -->\n\n# Guide\n\n* [About](about-this-bundle.md) - About.\n");
            Write("bundles/alpha/log.md", "# Log\n\n## 2026-08-15\n\n**Opened.**\n");
            Write("bundles/alpha/about-this-bundle.md", Concept("About Alpha", "The alpha bundle."));
            Write("bundles/alpha/topics/index.md", "<!-- generated by okf -->\n\n# Concept\n\n* [Widgets](widgets.md) - Widgets.\n");
            Write("bundles/alpha/topics/about.md", Concept("About", "The topics directory."));
            Write(
                "bundles/alpha/topics/widgets.md",
                Concept("Widgets", "Widgets, and [gadgets](../../beta/gadgets.md) next door."));
            Write("bundles/alpha/references/attester.py", "print('an attester is bundle content')\n");
            Write("bundles/beta/index.md", "<!-- generated by okf -->\n\n# Concept\n\n* [Gadgets](gadgets.md) - Gadgets.\n");
            Write("bundles/beta/gadgets.md", Concept("Gadgets", "Gadgets."));

            // Editor and tool droppings inside a bundle: never knowledge, never shipped.
            Write("bundles/alpha/topics/widgets.md~", "an editor backup");
            Write("bundles/alpha/.obsidian/workspace.json", "{}");
            Write("bundles/alpha/.hidden.md", "---\ntype: Concept\n---\n");
            Write("bundles/alpha/topics/.widgets.md.swp", "swap");
            Write("bundles/alpha/merge.md.orig", "a merge leftover");

            // The vault around the bundles: producer-side, every byte of it.
            Write("okf.json", "{ \"lint\": { \"severities\": { \"OKF0301\": \"error\" } } }");
            Write("README.md", "# Vault readme, outside every bundle root.\n");
            Write(".markdownlint.yaml", "MD025: false\n");
            Write("custodian/recipe.json", "{ \"skills\": [] }");
            Write("custodian/check-manifest.py", "print('producer-side')\n");
            Write("raw/manifest.json", "{ \"manifestVersion\": 1, \"captures\": [] }");
            Write("raw/2026-08-15-thing.html", "<html>evidence</html>");
        }

        /// <summary>The vault root, <c>&lt;project&gt;/okf</c>.</summary>
        public string Root { get; }

        /// <summary>A directory to write distributions into, outside the vault.</summary>
        public string Output { get; }

        /// <summary>The name the manifest should record for this vault.</summary>
        public string VaultName => "vault-project";

        public string Write(string relativePath, string content)
        {
            var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        /// <summary>Re-stamps every file in the vault, the way a fresh checkout would.</summary>
        public void Touch(DateTime when)
        {
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                File.SetLastWriteTimeUtc(file, when);
            }
        }

        public string CreateDirectory(string relativePath)
        {
            var path = Path.Combine(this.parent, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(path);
            return path;
        }

        public OkfWorkingSet WorkingSet() =>
            OkfDiscovery.Resolve(Root, new OkfEnvironment(this.parent));

        public void Dispose()
        {
            try
            {
                Directory.Delete(this.parent, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not worth failing a test over.
            }
        }
    }
}
