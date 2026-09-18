using DocOps.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocOps.Infrastructure.Persistence.Configurations;

public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.LastName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.JobTitle).HasMaxLength(150);
        builder.Property(u => u.ExternalOrganisation).HasMaxLength(200);
        builder.Property(u => u.PreferredCulture).HasMaxLength(16).IsRequired();
        builder.Property(u => u.RowVersion).IsRowVersion();

        builder.Ignore(u => u.FullName);
        builder.Ignore(u => u.Initials);

        builder.HasOne(u => u.Tenant)
            .WithMany()
            .HasForeignKey(u => u.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(u => new { u.TenantId, u.IsActive });
        builder.HasIndex(u => u.UserType);
    }
}

public sealed class ApplicationRoleConfiguration : IEntityTypeConfiguration<ApplicationRole>
{
    public void Configure(EntityTypeBuilder<ApplicationRole> builder) =>
        builder.Property(r => r.Description).HasMaxLength(300);
}

public sealed class UserDepartmentConfiguration : IEntityTypeConfiguration<UserDepartment>
{
    public void Configure(EntityTypeBuilder<UserDepartment> builder)
    {
        builder.ToTable("UserDepartments");
        builder.HasKey(ud => new { ud.UserId, ud.DepartmentId });

        builder.HasOne(ud => ud.User)
            .WithMany(u => u.Departments)
            .HasForeignKey(ud => ud.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ud => ud.Department)
            .WithMany(d => d.Members)
            .HasForeignKey(ud => ud.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        // A user has at most one primary department.
        builder.HasIndex(ud => ud.UserId)
            .IsUnique()
            .HasFilter("[IsPrimary] = 1")
            .HasDatabaseName("IX_UserDepartments_SinglePrimary");
    }
}
