using System.Collections.Generic;

namespace GameKit.Core.Contracts
{
    /// <summary>
    /// Upgrades a save from <see cref="FromVersion"/> to <c>FromVersion + 1</c>.
    /// <paramref name="entries"/> maps each save key to its raw JSON, so a migration can rename keys
    /// or rewrite JSON without the old C# types still existing.
    /// </summary>
    public interface ISaveMigration
    {
        int FromVersion { get; }

        void Migrate(IDictionary<string, string> entries);
    }
}
