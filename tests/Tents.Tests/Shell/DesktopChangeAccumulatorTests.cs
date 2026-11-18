using Tents.Core;
using Tents.Shell.Desktop;

namespace Tents.Tests.Shell;

public class DesktopChangeAccumulatorTests
{
    private const string _folder = @"C:\Users\Someone\Desktop\";

    private readonly DesktopChangeAccumulator _accumulator = new();

    [Fact]
    public void Create_IsAnAddition()
    {
        Add(RawChangeKind.Created, "a.txt");

        var batch = _accumulator.TakeBatch();

        Assert.Equal([Id("a.txt")], batch.Added);
        Assert.Empty(batch.Removed);
    }

    [Fact]
    public void CreateThenChange_IsJustAnAddition()
    {
        Add(RawChangeKind.Created, "a.txt");
        Add(RawChangeKind.Changed, "a.txt");
        Add(RawChangeKind.Changed, "a.txt");

        var batch = _accumulator.TakeBatch();

        Assert.Equal([Id("a.txt")], batch.Added);
        Assert.Empty(batch.Changed);
    }

    [Fact]
    public void CreateThenDelete_IsNothing()
    {
        Add(RawChangeKind.Created, "tmp.txt");
        Add(RawChangeKind.Changed, "tmp.txt");
        Add(RawChangeKind.Deleted, "tmp.txt");

        Assert.True(_accumulator.TakeBatch().IsEmpty);
    }

    [Fact]
    public void DeleteThenCreate_IsAChange()
    {
        // Typical "save by replace": the item stays, its content changed.
        Add(RawChangeKind.Deleted, "doc.txt");
        Add(RawChangeKind.Created, "doc.txt");

        var batch = _accumulator.TakeBatch();

        Assert.Equal([Id("doc.txt")], batch.Changed);
        Assert.Empty(batch.Added);
        Assert.Empty(batch.Removed);
    }

    [Fact]
    public void RepeatedChanges_AreOneChange()
    {
        Add(RawChangeKind.Changed, "a.txt");
        Add(RawChangeKind.Changed, "A.TXT");

        Assert.Equal([Id("a.txt")], _accumulator.TakeBatch().Changed);
    }

    [Fact]
    public void Rename_CarriesOldAndNewId()
    {
        Rename("old.txt", "new.txt");

        var batch = _accumulator.TakeBatch();

        Assert.Equal([(Id("old.txt"), Id("new.txt"))], batch.Renamed);
        Assert.Empty(batch.Added);
        Assert.Empty(batch.Removed);
    }

    [Fact]
    public void RenameChain_IsOneRename()
    {
        Rename("a.txt", "b.txt");
        Rename("b.txt", "c.txt");

        Assert.Equal([(Id("a.txt"), Id("c.txt"))], _accumulator.TakeBatch().Renamed);
    }

    [Fact]
    public void RenameBackToTheOriginalName_IsNothing()
    {
        Rename("a.txt", "b.txt");
        Rename("b.txt", "a.txt");

        Assert.True(_accumulator.TakeBatch().IsEmpty);
    }

    [Fact]
    public void RenameOfANewItem_IsAnAdditionUnderTheLastName()
    {
        // Browsers and editors write a temporary name, then rename it.
        Add(RawChangeKind.Created, "download.tmp");
        Rename("download.tmp", "photo.jpg");

        var batch = _accumulator.TakeBatch();

        Assert.Equal([Id("photo.jpg")], batch.Added);
        Assert.Empty(batch.Renamed);
    }

    [Fact]
    public void RenameThenDelete_RemovesTheOriginal()
    {
        Rename("a.txt", "b.txt");
        Add(RawChangeKind.Deleted, "b.txt");

        var batch = _accumulator.TakeBatch();

        Assert.Equal([Id("a.txt")], batch.Removed);
        Assert.Empty(batch.Renamed);
    }

    [Fact]
    public void ChangeThenRename_ReportsTheChangeUnderTheNewId()
    {
        Add(RawChangeKind.Changed, "a.txt");
        Rename("a.txt", "b.txt");

        var batch = _accumulator.TakeBatch();

        Assert.Equal([(Id("a.txt"), Id("b.txt"))], batch.Renamed);
        Assert.Equal([Id("b.txt")], batch.Changed);
    }

    [Fact]
    public void RenameOntoAnExistingItem_RemovesTheReplacedOne()
    {
        Add(RawChangeKind.Changed, "target.txt");
        Rename("source.txt", "target.txt");

        var batch = _accumulator.TakeBatch();

        Assert.Equal([Id("target.txt")], batch.Removed);
        Assert.Equal([(Id("source.txt"), Id("target.txt"))], batch.Renamed);
    }

    [Fact]
    public void CasingOnlyRename_KeepsTheId_AndIsAChange()
    {
        Rename("report.txt", "Report.txt");

        var batch = _accumulator.TakeBatch();

        Assert.Empty(batch.Renamed);
        Assert.Equal([Id("report.txt")], batch.Changed);
        Assert.Equal(_folder + "Report.txt", batch.Changed[0].Value);
    }

    [Fact]
    public void Overflow_AsksForARescan()
    {
        Add(RawChangeKind.Created, "a.txt");
        _accumulator.Add(new RawChange(RawChangeKind.Overflow));

        var batch = _accumulator.TakeBatch();

        Assert.True(batch.RescanNeeded);
        Assert.False(batch.IsEmpty);
        Assert.Equal([Id("a.txt")], batch.Added);
    }

    [Fact]
    public void TakeBatch_StartsANewBurst()
    {
        Add(RawChangeKind.Created, "a.txt");
        _accumulator.Add(new RawChange(RawChangeKind.Overflow));
        _accumulator.TakeBatch();

        Assert.True(_accumulator.IsEmpty);
        Assert.True(_accumulator.TakeBatch().IsEmpty);
    }

    [Fact]
    public void ManyCreates_AreOneBatch()
    {
        for (var i = 0; i < 20; i++)
        {
            Add(RawChangeKind.Created, $"file-{i}.txt");
        }

        Assert.Equal(20, _accumulator.TakeBatch().Added.Count);
    }

    [Fact]
    public void MissingPath_IsRejected() =>
        Assert.Throws<ArgumentException>(() => _accumulator.Add(new RawChange(RawChangeKind.Created)));

    private static ItemId Id(string name) => new(_folder + name);

    private void Add(RawChangeKind kind, string name) => _accumulator.Add(new RawChange(kind, _folder + name));

    private void Rename(string from, string to) =>
        _accumulator.Add(new RawChange(RawChangeKind.Renamed, _folder + to, _folder + from));
}
