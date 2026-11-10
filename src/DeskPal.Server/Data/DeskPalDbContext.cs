using Microsoft.EntityFrameworkCore;

namespace DeskPal.Server.Data;

public sealed class DeskPalDbContext(DbContextOptions<DeskPalDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<RoomMember> RoomMembers => Set<RoomMember>();
    public DbSet<RoomInvite> RoomInvites => Set<RoomInvite>();
    public DbSet<BuiltinRule> BuiltinRules => Set<BuiltinRule>();
    public DbSet<RoomRule> RoomRules => Set<RoomRule>();
    public DbSet<AppMeta> AppMeta => Set<AppMeta>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.UserName).IsUnique();
            e.Property(x => x.UserName).HasMaxLength(32);
            e.Property(x => x.DisplayName).HasMaxLength(20);
        });

        b.Entity<Room>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(30);
            e.HasIndex(x => x.OwnerId);
        });

        b.Entity<RoomMember>(e =>
        {
            e.HasKey(x => new { x.RoomId, x.UserId });
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.Room).WithMany(x => x.Members).HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RoomInvite>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.RoomId);
            e.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<BuiltinRule>(e => e.HasIndex(x => new { x.Kind, x.Pattern }).IsUnique());

        b.Entity<RoomRule>(e =>
        {
            e.HasIndex(x => new { x.RoomId, x.Kind, x.Pattern }).IsUnique();
            e.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AppMeta>(e => e.HasKey(x => x.Key));
    }
}