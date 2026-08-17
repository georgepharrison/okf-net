// Vertical slicing (#59) split this project's single flat namespace into one namespace
// per capability folder. A handful of global usings — one per folder — keeps every file
// free of a repetitive using block for its siblings, while still letting a reader jump to
// a specific file's own `using` list to see any cross-capability dependency it has (the
// pattern AGENTS.md and docs/architecture.md's Consistency Conventions call for).
global using Okf.Cli.Bundle;
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
