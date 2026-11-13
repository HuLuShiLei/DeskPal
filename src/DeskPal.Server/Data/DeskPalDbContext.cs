using Microsoft.EntityFrameworkCore;

namespace DeskPal.Server.Data;

public sealed class DeskPalDbContext(DbContextOptions<DeskPalDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<RoomMember> RoomMembers => Set<RoomMember>();
    public DbSet<RoomInvite> RoomInvites => Set<RoomInvite>();
    public DbSet<RoomBan> RoomBans => Set<RoomBan>();
    public DbSet<RoomBlock> RoomBlocks => Set<RoomBlock>();
    public DbSet<BuiltinRule> BuiltinRules => Set<BuiltinRule>();
    public DbSet<UserRule> UserRules => Set<UserRule>();
    public DbSet<UnknownAppReport> UnknownAppReports => Set<UnknownAppReport>();
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

        b.Entity<RoomBan>(e =>
        {
            e.HasKey(x => new { x.RoomId, x.UserId });
            e.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RoomBlock>(e =>
        {
            e.HasKey(x => new { x.RoomId, x.BlockerId, x.BlockedId });
            e.HasIndex(x => new { x.RoomId, x.BlockedId });
            e.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.BlockerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.BlockedId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<BuiltinRule>(e => e.HasIndex(x => new { x.Kind, x.Pattern }).IsUnique());

        b.Entity<UserRule>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.Kind, x.Pattern }).IsUnique();
            e.HasIndex(x => new { x.Kind, x.Pattern });
            e.Property(x => x.Pattern).HasMaxLength(200);
            e.Property(x => x.Name).HasMaxLength(60);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<UnknownAppReport>(e =>
        {
            e.HasKey(x => new { x.UserId, x.Kind, x.Pattern });
            e.Property(x => x.Pattern).HasMaxLength(200);
            e.Property(x => x.DisplayName).HasMaxLength(60);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AppMeta>(e => e.HasKey(x => x.Key));
    }
}
