using GameKit.Core.Contracts;

namespace GameKit.Save
{
    /// <summary>
    /// Save format version and the steps to upgrade old saves.
    /// When a save class changes shape (renamed field / key, changed meaning): bump <see cref="CurrentVersion"/>,
    /// add an <see cref="ISaveMigration"/> with <c>FromVersion = old version</c>, and add a test with an old save file.
    /// Adding a new field with a default value needs no migration.
    /// </summary>
    public static class SaveMigrations
    {
        public const int CurrentVersion = 1;

        public static ISaveMigration[] All => new ISaveMigration[]
        {
            // new RenameBoosterKeys(), // FromVersion = 1
        };
    }
}
