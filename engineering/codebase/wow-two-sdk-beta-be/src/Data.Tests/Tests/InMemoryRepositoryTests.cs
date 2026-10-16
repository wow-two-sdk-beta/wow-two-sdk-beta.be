using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Repositories;

namespace WoW.Two.Sdk.Beta.Be.Data.Tests.Tests;

/// <summary>The in-memory repository honours the repository contract by id, with no database.</summary>
public sealed class InMemoryRepositoryTests
{
    [Fact]
    public async Task Crud_ShouldMatchRowsById()
    {
        IRepository<Note, int> repository = new InMemoryRepository<Note, int>();
        await repository.CreateAsync(new Note(1, "first"));
        await repository.CreateRangeAsync([new Note(2, "second"), new Note(3, "third")]);

        await repository.UpdateAsync(new Note(2, "edited"));
        await repository.DeleteAsync(new Note(3, "a different instance"));

        (await repository.GetByIdAsync(2))!.Text.Should().Be("edited");
        (await repository.ExistsAsync(3)).Should().BeFalse();
        (await repository.CountAsync()).Should().Be(2);
        (await repository.GetAllAsync()).Select(note => note.Id).Should().Equal(1, 2);
        (await repository.DeleteByIdAsync(1)).Should().BeTrue();
        (await repository.DeleteByIdAsync(1)).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_ShouldIgnoreAnUnstoredRow()
    {
        var repository = new InMemoryRepository<Note, int>();

        await repository.UpdateAsync(new Note(9, "ghost"));

        repository.Rows.Should().BeEmpty();
    }

    private sealed record Note(int Id, string Text) : IKeyedEntity<int>;
}
