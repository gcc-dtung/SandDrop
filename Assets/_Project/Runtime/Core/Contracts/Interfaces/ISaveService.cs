namespace GameKit.Core.Contracts
{
    /// <summary>
    /// Key/value save store. Each feature owns one <c>[Serializable]</c> class with a <c>const string Key</c>
    /// and loads / saves it through this service. Data is serialized with <c>JsonUtility</c>,
    /// so the type must be a Unity-serializable class (not a bare int / string / Dictionary).
    /// <see cref="Save{T}"/> only updates memory; <see cref="Flush"/> writes to disk.
    /// </summary>
    public interface ISaveService
    {
        bool Has(string key);

        bool TryLoad<T>(string key, out T data);

        /// <summary>Returns the stored data, or a new instance (field defaults) when the key is missing or unreadable.</summary>
        T Load<T>(string key) where T : new();

        void Save<T>(string key, T data);

        void Delete(string key);

        void DeleteAll();

        /// <summary>True when there are changes not yet written by <see cref="Flush"/>.</summary>
        bool IsDirty { get; }

        /// <summary>Write pending changes to disk. Does nothing when nothing changed.</summary>
        void Flush();
    }
}
