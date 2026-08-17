// Vertical slicing (#59) split this project's single flat namespace into one namespace
// per capability folder. A handful of global usings — one per folder — keeps every file
// free of a repetitive using block for its siblings, while still letting a reader jump to
// a specific file's own `using` list to see any cross-capability dependency it has (the
// pattern AGENTS.md and docs/architecture.md's Consistency Conventions call for). Applied
// consistently with the same pattern in Okf.Cli/GlobalUsings.cs.
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
