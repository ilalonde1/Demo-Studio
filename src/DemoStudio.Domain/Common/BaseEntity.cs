namespace DemoStudio.Domain.Common;

public abstract class BaseEntity
{
    protected BaseEntity(Guid id)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CreatedUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; protected set; }

    public DateTimeOffset CreatedUtc { get; private set; }

    public DateTimeOffset? UpdatedUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    public void MarkUpdated()
    {
        UpdatedUtc = DateTimeOffset.UtcNow;
    }
}
