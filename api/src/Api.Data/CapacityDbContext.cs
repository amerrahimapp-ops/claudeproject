using Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Api.Data;

public class CapacityDbContext : DbContext
{
    public CapacityDbContext(DbContextOptions<CapacityDbContext> options)
        : base(options)
    {
    }

    public DbSet<Request> Requests => Set<Request>();
    public DbSet<RequestServer> RequestServers => Set<RequestServer>();
    public DbSet<Justification> Justifications => Set<Justification>();
    public DbSet<WorkflowStage> WorkflowStages => Set<WorkflowStage>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<WorkflowConfig> WorkflowConfigs => Set<WorkflowConfig>();
    public DbSet<AiEvaluation> AiEvaluations => Set<AiEvaluation>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(u => u.AdUsername).IsUnique();
            entity.Property(u => u.AdUsername).HasMaxLength(255).IsRequired();
            entity.Property(u => u.PfNumber).HasMaxLength(255);
            entity.Property(u => u.DisplayName).HasMaxLength(255).IsRequired();
            entity.Property(u => u.Email).HasMaxLength(255).IsRequired();
            entity.Property(u => u.Role).HasConversion<string>().HasMaxLength(50);
        });

        modelBuilder.Entity<Request>(entity =>
        {
            entity.HasIndex(r => r.RequestNumber).IsUnique();
            entity.Property(r => r.RequestNumber).HasMaxLength(20).IsRequired();
            entity.Property(r => r.Status).HasConversion<string>().HasMaxLength(50);
            entity.Property(r => r.Environment).HasConversion<string>().HasMaxLength(20);
            entity.Property(r => r.ProjectType).HasConversion<string>().HasMaxLength(20);
            entity.Property(r => r.Priority).HasConversion<string>().HasMaxLength(20);
            entity.Property(r => r.CurrentCapacity).HasColumnType("json");
            entity.Property(r => r.RequestedCapacity).HasColumnType("json");
            entity.Property(r => r.UpliftPercentages).HasColumnType("json");
            entity.Property(r => r.ConcurrencyVersion).IsConcurrencyToken();

            entity.HasOne(r => r.RequestorUser)
                .WithMany(u => u.Requests)
                .HasForeignKey(r => r.RequestorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RequestServer>(entity =>
        {
            entity.Property(rs => rs.Hostname).HasMaxLength(255).IsRequired();
            entity.Property(rs => rs.ResourceType).HasConversion<string>().HasMaxLength(20);
            entity.Property(rs => rs.Platform).HasConversion<string>().HasMaxLength(20);
            entity.Property(rs => rs.MountPoint).HasMaxLength(255);
            entity.Property(rs => rs.AppTier).HasMaxLength(255);
            entity.Property(rs => rs.CurrentValue).HasColumnType("decimal(18,2)");
            entity.Property(rs => rs.RequestedValue).HasColumnType("decimal(18,2)");

            entity.HasOne(rs => rs.Request)
                .WithMany(r => r.RequestServers)
                .HasForeignKey(rs => rs.RequestId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Justification>(entity =>
        {
            entity.Property(j => j.ResourceType).HasConversion<string>().HasMaxLength(20);
            entity.Property(j => j.QuestionKey).HasMaxLength(255).IsRequired();
            entity.Property(j => j.AnswerText).HasColumnType("text").IsRequired();

            entity.HasOne(j => j.Request)
                .WithMany(r => r.Justifications)
                .HasForeignKey(j => j.RequestId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkflowStage>(entity =>
        {
            entity.Property(ws => ws.StageName).HasMaxLength(255).IsRequired();
            entity.Property(ws => ws.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(ws => ws.AssignedRole).HasMaxLength(50);
            entity.Property(ws => ws.Comments).HasColumnType("text");

            entity.HasOne(ws => ws.Request)
                .WithMany(r => r.WorkflowStages)
                .HasForeignKey(ws => ws.RequestId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Attachment>(entity =>
        {
            entity.Property(a => a.FileName).HasMaxLength(255).IsRequired();
            entity.Property(a => a.StoragePath).HasMaxLength(1024).IsRequired();
            entity.Property(a => a.ContentType).HasMaxLength(255).IsRequired();

            entity.HasOne(a => a.Request)
                .WithMany(r => r.Attachments)
                .HasForeignKey(a => a.RequestId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.UploadedByUser)
                .WithMany()
                .HasForeignKey(a => a.UploadedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.Property(al => al.EntityType).HasMaxLength(255).IsRequired();
            entity.Property(al => al.Action).HasMaxLength(100).IsRequired();
            entity.Property(al => al.OldValues).HasColumnType("json");
            entity.Property(al => al.NewValues).HasColumnType("json");

            entity.HasOne(al => al.PerformedByUser)
                .WithMany()
                .HasForeignKey(al => al.PerformedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserPreference>(entity =>
        {
            entity.HasIndex(up => up.UserId).IsUnique();
            entity.Property(up => up.NotificationPrefs).HasColumnType("json").IsRequired();
            entity.Property(up => up.DefaultView).HasMaxLength(100).IsRequired();
            entity.Property(up => up.Theme).HasConversion<string>().HasMaxLength(20);

            entity.HasOne(up => up.User)
                .WithOne(u => u.UserPreference)
                .HasForeignKey<UserPreference>(up => up.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkflowConfig>(entity =>
        {
            entity.Property(wc => wc.StageName).HasMaxLength(255).IsRequired();
            entity.Property(wc => wc.AllowedTransitions).HasColumnType("json").IsRequired();
            entity.Property(wc => wc.RequiredRole).HasMaxLength(50);
            entity.Property(wc => wc.ValidationRules).HasColumnType("json");
            entity.Property(wc => wc.NotificationTemplateId).HasMaxLength(100);
        });

        modelBuilder.Entity<AiEvaluation>(entity =>
        {
            entity.Property(ae => ae.Prompt).HasColumnType("text").IsRequired();
            entity.Property(ae => ae.RawResponse).HasColumnType("text").IsRequired();
            entity.Property(ae => ae.Recommendation).HasMaxLength(50);
            entity.Property(ae => ae.FlagsJson).HasColumnType("json");

            entity.HasOne(ae => ae.Request)
                .WithMany()
                .HasForeignKey(ae => ae.RequestId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.Property(om => om.MessageType).HasMaxLength(50).IsRequired();
            entity.Property(om => om.Payload).HasColumnType("json").IsRequired();
            entity.Property(om => om.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(om => om.LastError).HasColumnType("text");

            entity.HasIndex(om => new { om.Status, om.CreatedAt });
        });
    }
}
