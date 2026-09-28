using DocOps.Domain.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocOps.Infrastructure.Persistence.Configurations;

public sealed class BackgroundJobRunConfiguration : IEntityTypeConfiguration<BackgroundJobRun>
{
    public void Configure(EntityTypeBuilder<BackgroundJobRun> builder)
    {
        builder.ToTable("BackgroundJobRuns");

        builder.Property(run => run.Id).ValueGeneratedNever();
        builder.Property(run => run.Job).HasMaxLength(64).IsRequired();

        // Long enough to say what went wrong, short enough that nothing tries to fit a stack in it.
        builder.Property(run => run.Failure).HasMaxLength(1024);

        // Every question asked of this table is «what happened lately, for this job»: the newest
        // rows of one job, and the failures of the last day across all of them.
        builder.HasIndex(run => new { run.Job, run.StartedAtUtc });
        builder.HasIndex(run => run.StartedAtUtc);
    }
}
