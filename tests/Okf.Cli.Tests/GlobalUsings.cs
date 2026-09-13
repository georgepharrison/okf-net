// Mirrors src/Okf.Cli/GlobalUsings.cs: one global using per Okf.Cli capability
// namespace, plus one per this project's own mirrored test-folder namespace, so a test
// moved into a folder under #59 keeps seeing the same names it always did without a
// per-file using block. Okf.Core.* is included too since several Cli tests reach into
// Core types directly (e.g. OkfScopeKind, OkfDocument fixtures).
global using Okf.Cli.Bundle;
global using Okf.Cli.Candidates;
global using Okf.Cli.Capture;
global using Okf.Cli.Completion;
global using Okf.Cli.Generated;
global using Okf.Cli.Inbox;
global using Okf.Cli.Index;
global using Okf.Cli.Init;
global using Okf.Cli.Lint;
global using Okf.Cli.Mcp;
global using Okf.Cli.Registry;
global using Okf.Cli.Search;
global using Okf.Cli.Shared;
global using Okf.Cli.Site;
global using Okf.Cli.Skills;
global using Okf.Cli.Tests.Bundle;
global using Okf.Cli.Tests.Candidates;
global using Okf.Cli.Tests.Capture;
global using Okf.Cli.Tests.Completion;
global using Okf.Cli.Tests.Generated;
global using Okf.Cli.Tests.Inbox;
global using Okf.Cli.Tests.Index;
global using Okf.Cli.Tests.Init;
global using Okf.Cli.Tests.Lint;
global using Okf.Cli.Tests.Mcp;
global using Okf.Cli.Tests.Registry;
global using Okf.Cli.Tests.Search;
global using Okf.Cli.Tests.Shared;
global using Okf.Cli.Tests.Site;
global using Okf.Cli.Tests.Skills;
global using Okf.Cli.Tests.Upgrade;
global using Okf.Cli.Tests.Verify;
global using Okf.Cli.Upgrade;
global using Okf.Cli.Verify;
global using Okf.Core.Bundle;
global using Okf.Core.Capture;
global using Okf.Core.Documents;
global using Okf.Core.Index;
global using Okf.Core.Lint;
global using Okf.Core.Search;
global using Okf.Core.Site;
global using Okf.Core.Skills;
global using Okf.Core.Trust;
global using Okf.Core.Upgrade;
global using Okf.Core.Vault;
