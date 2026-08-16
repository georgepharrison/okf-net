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
                "bundles/alpha/.hidden.md",
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

    /// <summary>
    /// Every header block in the archive names a file the plan named. The point is what it
    /// rules out: a tar writer may emit bookkeeping entries of its own — .NET's PAX format
    /// writes one per file, named <c>./PaxHeaders.&lt;process-id&gt;/.</c> — and an entry
    /// whose name okf-net does not control is an entry that can differ between two builds
    /// of the same vault. Reading the raw blocks is deliberate: <c>TarReader</c> consumes
    /// extended headers silently, which is exactly how a byte-comparison inside one
    /// process can pass while two release builds disagree.
    /// </summary>
    [Fact]
    public void The_archive_carries_no_bookkeeping_entry_the_bundler_did_not_name()
    {
        using var tree = new BundlerVault();
        var archive = Path.Combine(tree.Output, "bundle.tar.gz");
        var plan = OkfBundler.Plan(tree.WorkingSet(), Options());

        OkfBundler.Write(plan, archive, OkfDistributionFormat.TarGz);

        var expected = plan.Entries.Select(entry => entry.Path)
            .Append(OkfDistributionManifest.FileName)
            .Order(StringComparer.Ordinal);
        Assert.Equal(expected, RawTarNames(archive));
    }

    /// <summary>
    /// The long-path case the GNU-over-ustar decision rests on. Ustar throws outright on a
    /// path it cannot split across its 100-byte name and 155-byte prefix; GNU writes one
    /// through a preceding <c>././@LongLink</c> block. That block is the one entry the
    /// bundler does not name, so the reproducibility claim depends on its name being a
    /// <em>constant</em> rather than something like PAX's pid — which is asserted here by
    /// packaging the same vault twice and comparing bytes, with the archive's own header
    /// blocks read raw so the extra entry cannot hide.
    /// </summary>
    [Fact]
    public void A_path_too_long_for_ustar_is_written_through_a_constant_named_long_link_entry()
    {
        using var tree = new BundlerVault();
        var deep = "bundles/alpha/" + string.Join('/', Enumerable.Repeat(new string('d', 30), 8));
        tree.Write(deep + "/a-concept-with-a-very-long-path.md", Concept("Deep", "Far down."));
        var first = Path.Combine(tree.Output, "first.tar.gz");
        var second = Path.Combine(tree.Output, "second.tar.gz");
        var plan = OkfBundler.Plan(tree.WorkingSet(), Options());

        OkfBundler.Write(plan, first, OkfDistributionFormat.TarGz);
        OkfBundler.Write(OkfBundler.Plan(tree.WorkingSet(), Options()), second, OkfDistributionFormat.TarGz);

        // The path really is past what ustar can hold, or the test proves nothing.
        var packaged = Assert.Single(plan.Entries, entry => entry.Path.Length > 100);
        Assert.Equal(deep + "/a-concept-with-a-very-long-path.md", packaged.Path);

        // Every raw header name is either one the plan named, the truncated head of one
        // (which is what GNU leaves in the 100-byte name field of a long-named entry), or
        // the long-link block itself — whose name must be that constant and nothing
        // host-derived. The long-link block must actually be there, or the archive never
        // exercised the case.
        var named = plan.Entries.Select(entry => entry.Path)
            .Append(OkfDistributionManifest.FileName)
            .ToList();
        var names = RawTarNames(first).ToList();
        Assert.Contains("././@LongLink", names);
        Assert.All(names, name => Assert.True(
            name == "././@LongLink"
            || named.Any(known => known.StartsWith(name, StringComparison.Ordinal)),
            $"the archive carries an entry named '{name}', which the bundler did not name."));

        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
        Assert.True(OkfBundler.Verify(first).IsValid);
    }

    [Fact]
    public void Archive_entries_carry_no_owner_and_a_normalized_mode()
    {
        using var tree = new BundlerVault();
        var archive = Path.Combine(tree.Output, "bundle.tar.gz");

        OkfBundler.Write(OkfBundler.Plan(tree.WorkingSet(), Options()), archive, OkfDistributionFormat.TarGz);

        foreach (var entry in TarEntries(archive).Cast<GnuTarEntry>())
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

    /// <summary>
    /// A bundle may hold an empty file — a placeholder, a truncated capture, a
    /// deliberately blank fixture — and the round trip has to survive it in every shape.
    /// tar stores such a file as a header with no data section at all, which
    /// <see cref="TarReader" /> surfaces as a null <c>DataStream</c>; reading that as
    /// "the entry is not a file" made <c>--verify</c> report the bundler's own tar.gz as
    /// missing a file it had just written, while zip and the directory shape passed.
    /// </summary>
    [Theory]
    [InlineData(OkfDistributionFormat.TarGz, "bundle.tar.gz")]
    [InlineData(OkfDistributionFormat.Zip, "bundle.zip")]
    [InlineData(OkfDistributionFormat.Directory, "dist")]
    public void Verify_accepts_a_zero_length_file_the_bundler_packaged(
        OkfDistributionFormat format,
        string name)
    {
        using var tree = new BundlerVault();
        tree.Write("bundles/alpha/references/placeholder.txt", string.Empty);
        var output = Path.Combine(tree.Output, name);
        var plan = OkfBundler.Plan(tree.WorkingSet(), Options());
        OkfBundler.Write(plan, output, format);

        var result = OkfBundler.Verify(output);

        // The empty file is genuinely in the plan — otherwise this would pass by
        // packaging nothing — and the distribution verifies clean with it there.
        Assert.Contains("bundles/alpha/references/placeholder.txt", plan.Entries.Select(entry => entry.Path));
        Assert.Empty(result.Findings);
        Assert.True(result.IsValid);
    }

    /// <summary>
    /// A tar entry that is not a file — a symlink, a hard link, a device node — carries no
    /// bytes, so it can never fail a digest comparison; skipping it therefore let one join
    /// a downloaded archive and still verify clean. A planted symlink is the interesting
    /// case, because extracting it writes a path into somebody else's filesystem, and it is
    /// exactly the "a file the distribution carries that the manifest never listed" tamper
    /// <c>--verify</c> exists to catch.
    /// </summary>
    [Theory]
    [InlineData(TarEntryType.SymbolicLink)]
    [InlineData(TarEntryType.HardLink)]
    public void Verify_reports_a_tar_entry_that_is_not_a_file(TarEntryType type)
    {
        using var tree = new BundlerVault();
        var output = Path.Combine(tree.Output, "bundle.tar.gz");
        OkfBundler.Write(OkfBundler.Plan(tree.WorkingSet(), Options()), output, OkfDistributionFormat.TarGz);
        Smuggle(output, "bundles/alpha/smuggled.md", type, "../../../../etc/passwd");

        var result = OkfBundler.Verify(output);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(OkfDistributionIssue.Unlisted, finding.Issue);
        Assert.Equal("bundles/alpha/smuggled.md", finding.Path);
        Assert.False(result.IsValid);
    }

    /// <summary>
    /// A cut-off download is the failure the digests exist for, so it must land as a
    /// finding and exit 1 rather than as an escaping <see cref="EndOfStreamException" />.
    /// That one is an <see cref="IOException" />, so unlike the not-an-archive case it did
    /// not crash — it fell through to the command's environment-failure handler and came
    /// out as exit 2 with a sentence naming no file.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Verify_reports_a_truncated_archive_rather_than_throwing(int divisor)
    {
        using var tree = new BundlerVault();
        var output = Path.Combine(tree.Output, "bundle.tar.gz");
        OkfBundler.Write(OkfBundler.Plan(tree.WorkingSet(), Options()), output, OkfDistributionFormat.TarGz);
        var whole = File.ReadAllBytes(output);
        File.WriteAllBytes(output, whole[..(divisor == 0 ? 0 : whole.Length / (divisor + 1))]);

        var result = OkfBundler.Verify(output);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(OkfDistributionIssue.Unreadable, finding.Issue);
        Assert.False(result.IsValid);
    }

    /// <summary>
    /// GNU's <c>atime</c> and <c>ctime</c> sit at byte 345 of a header block, which in
    /// ustar is where the <c>prefix</c> field begins — and CPython's <c>tarfile</c> joins
    /// <c>prefix</c> onto the entry name for every non-GNU-typed entry without checking the
    /// archive's magic first. Writing a real instant into them therefore made the Python
    /// standard library extract this archive into a directory named after the octal
    /// timestamp. GNU tar's own writer leaves the field NUL for a non-incremental entry;
    /// so does this one, and the expectation below is the byte range POSIX assigns to
    /// <c>prefix</c>, not a constant read back out of the code.
    /// </summary>
    [Fact]
    public void Tar_headers_leave_the_ustar_prefix_window_empty_so_every_reader_agrees_on_the_name()
    {
        using var tree = new BundlerVault();
        var archive = Path.Combine(tree.Output, "bundle.tar.gz");

        OkfBundler.Write(OkfBundler.Plan(tree.WorkingSet(), Options()), archive, OkfDistributionFormat.TarGz);

        var blocks = RawTarHeaderBlocks(archive).ToList();
        Assert.NotEmpty(blocks);
        foreach (var block in blocks)
        {
            Assert.Equal(new byte[155], block[345..500]);
        }
    }

    [Fact]
    public void Entries_are_sorted_by_path_not_by_bundle_then_path()
    {
        using var tree = new BundlerVault();
        tree.Write("bundles/alpha-extra/index.md", "<!-- generated by okf -->\n\n# Concept\n");
        tree.Write("bundles/alpha-extra/note.md", Concept("Note", "A note."));

        var plan = OkfBundler.Plan(tree.WorkingSet(), Options());

        // AD-36: entries sorted by path ordinally, the manifest sorting with them. `-`
        // (0x2D) sorts below `/` (0x2F), so `bundles/alpha-extra/…` comes before
        // `bundles/alpha/…` even though the bundle named `alpha` comes first — walking the
        // bundles in name order and appending is NOT the same order, and an archive whose
        // entries follow the walk instead of the sort is a different byte stream.
        Assert.Equal(
            plan.Entries.Select(entry => entry.Path).Order(StringComparer.Ordinal),
            plan.Entries.Select(entry => entry.Path));
        Assert.Equal(
            plan.Manifest.Files.Select(file => file.Path).Order(StringComparer.Ordinal),
            plan.Manifest.Files.Select(file => file.Path));
        Assert.Equal(
            ["bundles/alpha-extra/index.md", "bundles/alpha-extra/note.md", "bundles/alpha/.hidden.md"],
            plan.Entries.Select(entry => entry.Path).Take(3));
    }

    [Fact]
    public void A_lone_bundle_with_no_vault_around_it_names_no_vault_and_no_bundle_for_its_dangling_links()
    {
        using var bundle = new TempBundle("alpha");
        bundle.Add("a.md", "---\ntype: Concept\ntitle: A\n---\n\nA links to [b](../beta/b.md).\n");

        // `okf bundle <bundle-dir>` resolves one bundle with no vault around it, so there is
        // no `bundles/` tree in which to say which bundle a dangling target would have
        // landed in. AD-35 makes `sourceVault` a name, and there is no name to give.
        var plan = OkfBundler.Plan(new OkfWorkingSet([bundle.Bundle], null, "one bundle"), Options());

        Assert.Null(plan.Manifest.SourceVault);
        var link = Assert.Single(plan.Manifest.ExternalLinks);
        Assert.Equal("../beta/b.md", link.To);
        Assert.Null(link.Bundle);

        // Absent, and said so: a key the writer skipped would read as "not recorded" to a
        // consumer, and AD-34 requires every dangling link to be listed.
        var json = plan.Manifest.ToJson();
        Assert.Contains("\"sourceVault\": null", json, StringComparison.Ordinal);
        Assert.Contains("\"bundle\": null", json, StringComparison.Ordinal);
    }

    [Theory]
    // A manifest is data verify did not write. AD-4 makes okf-net tolerate a foreign
    // artifact, and a hand-edited or truncated digest is exactly what --verify exists to
    // notice — so it is reported, never indexed off the end of.
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", "0123456789ab…")]
    [InlineData("0123456789ab", "0123456789ab")]
    [InlineData("abc", "abc")]
    public void Verify_reports_a_recorded_digest_at_whatever_length_it_was_recorded(string recorded, string shown)
    {
        using var tree = new BundlerVault();
        var output = Path.Combine(tree.Output, "dist");
        OkfBundler.Write(OkfBundler.Plan(tree.WorkingSet(), Options()), output, OkfDistributionFormat.Directory);

        var manifestPath = Path.Combine(output, OkfDistributionManifest.FileName);
        var actual = OkfCaptureManifest.Sha256Of(
            Path.Combine(output, "bundles", "beta", "gadgets.md"));
        File.WriteAllText(
            manifestPath,
            File.ReadAllText(manifestPath).Replace(actual, recorded, StringComparison.Ordinal));

        var finding = Assert.Single(
            OkfBundler.Verify(output).Findings,
            finding => finding.Path == "bundles/beta/gadgets.md");

        Assert.Equal(OkfDistributionIssue.Modified, finding.Issue);
        Assert.Equal($"recorded {shown}, found {actual[..12]}….", finding.Detail);
    }

    [Fact]
    public void Verify_reports_a_path_that_is_neither_an_archive_nor_a_directory()
    {
        using var tree = new BundlerVault();

        // AD-37: a distribution that cannot be read is unreadable, never a pass. Verifying
        // the wrong path is the commonest way to reach this, and it has to be said out loud
        // rather than reported as a clean archive with nothing in it.
        var result = OkfBundler.Verify(Path.Combine(tree.Output, "not-there.tar.gz"));

        var finding = Assert.Single(result.Findings);
        Assert.Equal(OkfDistributionIssue.Unreadable, finding.Issue);
        Assert.Contains("neither an archive nor a directory", finding.Detail, StringComparison.Ordinal);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Verify_reports_a_manifest_that_does_not_read_as_a_manifest()
    {
        using var tree = new BundlerVault();
        var output = Path.Combine(tree.Output, "dist");
        OkfBundler.Write(OkfBundler.Plan(tree.WorkingSet(), Options()), output, OkfDistributionFormat.Directory);
        File.WriteAllText(Path.Combine(output, OkfDistributionManifest.FileName), "[]\n");

        var result = OkfBundler.Verify(output);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(OkfDistributionIssue.Unreadable, finding.Issue);
        Assert.Contains("does not parse as a distribution manifest", finding.Detail, StringComparison.Ordinal);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Verify_skips_a_directory_entry_in_a_tar_and_in_a_zip()
    {
        using var tree = new BundlerVault();
        var plan = OkfBundler.Plan(tree.WorkingSet(), Options());

        // AD-37: directory entries alone are skipped, because the bundler writes none and
        // every extractor makes the directories a file's path implies. An archive repacked
        // by another tool carries them, and reporting one as `unlisted` would fail a
        // distribution whose bytes are all exactly what the manifest says.
        var tar = Path.Combine(tree.Output, "dist.tar.gz");
        OkfBundler.Write(plan, tar, OkfDistributionFormat.TarGz);
        Repack(tar, writer => writer.WriteEntry(new GnuTarEntry(TarEntryType.Directory, "bundles/alpha/")));
        Assert.Equal([], OkfBundler.Verify(tar).Findings.Select(finding => finding.Path));

        var zip = Path.Combine(tree.Output, "dist.zip");
        OkfBundler.Write(plan, zip, OkfDistributionFormat.Zip);
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            archive.CreateEntry("bundles/alpha/");
        }

        Assert.Equal([], OkfBundler.Verify(zip).Findings.Select(finding => finding.Path));
    }

    /// <summary>Rewrites a tar.gz with one more entry appended, whatever kind of entry it is.</summary>
    private static void Repack(string archive, Action<TarWriter> append)
    {
        var entries = TarEntries(archive);
        using var file = File.Create(archive);
        using var gzip = new GZipStream(file, CompressionLevel.Optimal);
        using var writer = new TarWriter(gzip, TarEntryFormat.Gnu);
        foreach (var entry in entries)
        {
            writer.WriteEntry(entry);
        }

        append(writer);
    }

    [Fact]
    public void A_manifest_reads_every_key_only_in_the_shape_it_is_declared_with()
    {
        // AD-4 again: a foreign manifest is read, never assumed well-shaped. A key carrying
        // the wrong JSON type is the key being absent — not a cast that throws in the middle
        // of verifying somebody's download.
        var manifest = OkfDistributionManifest.Parse("""
            {
              "manifestVersion": "1",
              "okfVersion": "0.9",
              "generator": "okf/0.0.0",
              "sourceVault": 5,
              "generatedAt": "2026-08-15T14:00:00Z",
              "bundles": [],
              "externalLinks": [],
              "files": []
            }
            """);

        Assert.NotNull(manifest);
        Assert.Equal(OkfDistributionManifest.CurrentVersion, manifest.ManifestVersion);
        Assert.Null(manifest.SourceVault);

        // The spec version is the manifest's own claim about what it was written against,
        // so it is read rather than assumed: a distribution from a future spec has to be
        // able to say so.
        Assert.Equal("0.9", manifest.OkfVersion);
    }

    /// <summary>Appends one entry that is not a file to a written tar.gz.</summary>
    private static void Smuggle(string archive, string name, TarEntryType type, string linkName)
    {
        var entries = TarEntries(archive);
        using var file = File.Create(archive);
        using var gzip = new GZipStream(file, CompressionLevel.Optimal);
        using var writer = new TarWriter(gzip, TarEntryFormat.Gnu);
        foreach (var entry in entries)
        {
            writer.WriteEntry(entry);
        }

        writer.WriteEntry(new GnuTarEntry(type, name) { LinkName = linkName });
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

    /// <summary>
    /// Every name in the archive's raw 512-byte header blocks, sorted — including any a
    /// tar writer added for its own bookkeeping, which <see cref="TarReader" /> hides.
    /// </summary>
    private static IEnumerable<string> RawTarNames(string archive) =>
        RawTarHeaderBlocks(archive)
            .Select(block => Encoding.UTF8.GetString(block.AsSpan(0, 100).TrimEnd((byte)0)))
            .Order(StringComparer.Ordinal);

    /// <summary>Every 512-byte header block of a tar.gz, data blocks stepped over.</summary>
    private static List<byte[]> RawTarHeaderBlocks(string archive)
    {
        using var file = File.OpenRead(archive);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        gzip.CopyTo(raw);
        var bytes = raw.ToArray();

        var blocks = new List<byte[]>();
        for (var offset = 0; offset + 512 <= bytes.Length; offset += 512)
        {
            var block = bytes.AsSpan(offset, 512);
            if (block.TrimStart((byte)0).Length == 0)
            {
                // The two zero blocks that end an archive.
                break;
            }

            var size = Convert.ToInt64(
                Encoding.ASCII.GetString(block.Slice(124, 12)).Trim('\0', ' '),
                fromBase: 8);
            blocks.Add(block.ToArray());
            offset += (int)((size + 511) / 512) * 512;
        }

        return blocks;
    }

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
                using var writer = new TarWriter(gzip, TarEntryFormat.Gnu);
                foreach (var (name, text) in contents.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    writer.WriteEntry(new GnuTarEntry(TarEntryType.RegularFile, name)
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
            Write("bundles/alpha/.vscode/settings.json", "{}");
            Write("bundles/alpha/.idea/alpha.iml", "<module />");
            Write("bundles/alpha/.git/config", "[core]");
            Write("bundles/alpha/.DS_Store", "finder state");
            Write("bundles/alpha/Thumbs.db", "explorer state");
            Write("bundles/alpha/topics/.widgets.md.swp", "swap");
            Write("bundles/alpha/merge.md.orig", "a merge leftover");

            // A dot-prefixed concept is a concept: §11 holds it to conformance, so a
            // distribution that dropped it would ship a bundle the linter had judged.
            Write("bundles/alpha/.hidden.md", Concept("Hidden", "A dot-prefixed concept."));

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
