﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using ME3ExplorerCore.Gammtek.IO;
using ME3ExplorerCore.Misc;
using ME3ExplorerCore.TLK.ME1;
using ME3ExplorerCore.Unreal;
using Newtonsoft.Json;
using static ME3ExplorerCore.Unreal.UnrealFlags;

namespace ME3ExplorerCore.Packages
{
    public enum MEGame
    {
        Unknown = 0,
        ME1,
        ME2,
        ME3,
        UDK
    }

    public enum MELocalization
    {
        None = 0,
        INT,
        DEU,
        ESN,
        FRA,
        ITA,
        JPN,
        POL,
        RUS
    }

    public enum ArrayType
    {
        Object,
        Name,
        Enum,
        Struct,
        Bool,
        String,
        Float,
        Int,
        Byte,
        StringRef
    }

    [DebuggerDisplay("PropertyInfo | {Type} , reference: {Reference}, transient: {Transient}")]
    public class PropertyInfo : IEquatable<PropertyInfo>
    {
        //DO NOT CHANGE THE NAME OF ANY OF THESE fields/properties. THIS WILL BREAK JSON PARSING!
        public Unreal.PropertyType Type { get; }
        public string Reference { get; }
        public bool Transient { get; }

        public PropertyInfo(PropertyType type, string reference = null, bool transient = false)
        {
            Type = type;
            Reference = reference;
            Transient = transient;
        }

        public bool IsEnumProp() => Type == PropertyType.ByteProperty && Reference != null && Reference != "Class" && Reference != "Object";

        #region IEquatable

        public bool Equals(PropertyInfo other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Type == other.Type && string.Equals(Reference, other.Reference) && Transient == other.Transient;
        }

        public override bool Equals(object obj)
        {
            if (obj is null) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj.GetType() == this.GetType() && Equals((PropertyInfo) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (int) Type;
                hashCode = (hashCode * 397) ^ (Reference != null ? Reference.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ Transient.GetHashCode();
                return hashCode;
            }
        }

        public static bool operator ==(PropertyInfo left, PropertyInfo right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(PropertyInfo left, PropertyInfo right)
        {
            return !Equals(left, right);
        }

        #endregion
    }

    public class ClassInfo
    {
        //DO NOT CHANGE THE NAME OF ANY OF THESE fields/properties. THIS WILL BREAK JSON PARSING!
        [JsonIgnore]
        public string ClassName { get; set; }

        public OrderedMultiValueDictionary<string, PropertyInfo> properties = new OrderedMultiValueDictionary<string, PropertyInfo>();
        public string baseClass;
        //Relative to BIOGame
        public string pccPath;

        public int exportIndex;
        public bool isAbstract;

        public bool TryGetPropInfo(string name, MEGame game, out PropertyInfo propInfo) =>
            properties.TryGetValue(name, out propInfo) || (UnrealObjectInfo.GetClassOrStructInfo(game, baseClass)?.TryGetPropInfo(name, game, out propInfo) ?? false);
    }

    public interface IMEPackage : IDisposable
    {
        EPackageFlags Flags { get; }
        bool IsCompressed { get; }
        int NameCount { get; }
        int ExportCount { get; }
        int ImportCount { get; }
        int ImportOffset { get; }
        int ExportOffset { get; }
        int NameOffset { get; }
        /// <summary>
        /// The number of compressed chunks in the chunk table there were found during package loading.
        /// </summary>
        int NumCompressedChunksAtLoad { get; }
        Guid PackageGuid { get; set; }
        IReadOnlyList<ExportEntry> Exports { get; }
        IReadOnlyList<ImportEntry> Imports { get; }
        IReadOnlyList<string> Names { get; }
        MEGame Game { get; }
        MEPackage.GamePlatform Platform { get; }
        Endian Endian { get; }
        MELocalization Localization { get; }
        string FilePath { get; }
        DateTime LastSaved { get; }
        long FileSize { get; }

        //reading
        bool IsUExport(int index);
        bool IsName(int index);
        /// <summary>
        /// Checks if the specified UIndex is an import
        /// </summary>
        /// <param name="uindex"></param>
        /// <returns></returns>
        bool IsImport(int uindex);
        bool IsEntry(int uindex);
        /// <summary>
        ///     gets Export or Import entry, from unreal index. Can return null if index is 0
        /// </summary>
        /// <param name="index">unreal index</param>
        IEntry GetEntry(int index);

        /// <summary>
        /// Gets an export based on it's unreal based index in the export list.
        /// </summary>
        /// <param name="uIndex">unreal-based index in the export list</param>
        ExportEntry GetUExport(int uIndex);

        /// <summary>
        /// Gets an import based on it's unreal based index.
        /// </summary>
        /// <param name="uIndex">unreal-based index</param>
        ImportEntry GetImport(int uIndex);
        /// <summary>
        /// Try to get an ExportEntry by UIndex.
        /// </summary>
        /// <param name="uIndex"></param>
        /// <param name="export"></param>
        /// <returns></returns>
        bool TryGetUExport(int uIndex, out ExportEntry export);
        /// <summary>
        /// Try to get an ImportEntry by UIndex.
        /// </summary>
        /// <param name="uIndex"></param>
        /// <param name="import"></param>
        /// <returns></returns>
        bool TryGetImport(int uIndex, out ImportEntry import);
        /// <summary>
        /// Try to get an IEntry by UIndex.
        /// </summary>
        /// <param name="uIndex"></param>
        /// <param name="entry"></param>
        /// <returns></returns>
        bool TryGetEntry(int uIndex, out IEntry entry);

        int findName(string nameToFind);
        /// <summary>
        ///     gets Export or Import name, from unreal index
        /// </summary>
        /// <param name="index">unreal index</param>
        string getObjectName(int index);
        string GetNameEntry(int index);
        int GetNextIndexForName(string name);

        NameReference GetNextIndexedName(string name);
        //editing
        int FindNameOrAdd(string name);
        void replaceName(int index, string newName);
        void AddExport(ExportEntry exportEntry);
        void AddImport(ImportEntry importEntry);
        /// <summary>
        ///     exposed so that the property import function can restore the namelist after a failure.
        ///     please don't use it anywhere else.
        /// </summary>
        void restoreNames(List<string> list);

        /// <summary>
        /// Removes trashed imports and exports if they are at the end of their respective lists
        /// can only remove from the end because doing otherwise would mess up the indexing
        /// </summary>
        void RemoveTrailingTrash();

        byte[] getHeader();
        ObservableCollection<IPackageUser> Users { get; }
        List<IPackageUser> WeakUsers { get; }
        void RegisterTool(IPackageUser user);
        void Release(IPackageUser user = null);
        event UnrealPackageFile.MEPackageEventHandler noLongerOpenInTools;
        void RegisterUse();
        event UnrealPackageFile.MEPackageEventHandler noLongerUsed;
        MemoryStream SaveToStream(bool compress, bool includeAdditionalPackagesToCook = true, bool includeDependencyTable = true);
        List<ME1TalkFile> LocalTalkFiles { get; }
        public bool IsModified { get; internal set; }

        /// <summary>
        /// Compares this package against the one located on disk at the specified path
        /// </summary>
        /// <param name="packagePath"></param>
        /// <returns></returns>
        List<EntryStringPair> CompareToPackage(string packagePath);
        /// <summary>
        /// Compares this package against the specified other one
        /// </summary>
        /// <param name="compareFile"></param>
        /// <returns></returns>
        List<EntryStringPair> CompareToPackage(IMEPackage compareFile);
        /// <summary>
        /// Compares this package against the one in the specified stream
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        List<EntryStringPair> CompareToPackage(Stream stream);
    }
}