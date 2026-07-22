namespace MdExplorer.TagCloud.Models;

/// <summary>Sortierreihenfolge der Tag-Cloud-Darstellung.</summary>
public enum TagCloudSortOption
{
    /// <summary>Häufigste Tags zuerst (Default), bei Gleichstand alphabetisch nach Slug.</summary>
    Frequency = 0,

    /// <summary>Alphabetisch nach Slug aufsteigend.</summary>
    Alphabetical = 1,

    /// <summary>Zuletzt verwendete Tags zuerst (MAX <c>LastWriteTimeUtc</c> der Dateien).</summary>
    RecentlyUsed = 2,
}
