using System;
using Microsoft.EntityFrameworkCore;
using EtwMonitor.Core.Models;

namespace EtwMonitor.Core.Data
{
    public class MonitorDbContext : DbContext
    {
        public DbSet<SystemEvent> Events { get; set; } = null!;
        public DbSet<DetectedPattern> Patterns { get; set; } = null!;
        public DbSet<Diagnosis> Diagnoses { get; set; } = null!;

        public MonitorDbContext(DbContextOptions<MonitorDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // SystemEvent configuration
            modelBuilder.Entity<SystemEvent>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.HasIndex(e => e.Timestamp);
                entity.HasIndex(e => e.ProcessId);
                entity.HasIndex(e => new { e.Type, e.Result });
                
                // Store metadata as JSON
                entity.Property(e => e.Metadata)
                    .HasConversion(
                        v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                        v => System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, string>>(v, (System.Text.Json.JsonSerializerOptions?)null) 
                            ?? new System.Collections.Generic.Dictionary<string, string>()
                    );
            });

            // DetectedPattern configuration
            modelBuilder.Entity<DetectedPattern>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.HasIndex(e => e.PatternType);
                entity.HasIndex(e => e.Severity);
                entity.HasIndex(e => new { e.FirstSeen, e.LastSeen });
                
                // Store RelatedEventIds as JSON
                entity.Property(e => e.RelatedEventIds)
                    .HasConversion(
                        v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                        v => System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<long>>(v, (System.Text.Json.JsonSerializerOptions?)null) 
                            ?? new System.Collections.Generic.List<long>()
                    );
            });

            // Diagnosis configuration
            modelBuilder.Entity<Diagnosis>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.HasIndex(e => e.PatternId);
                entity.HasIndex(e => e.Timestamp);
                
                // Store PreventionMeasures as JSON
                entity.Property(e => e.PreventionMeasures)
                    .HasConversion(
                        v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                        v => System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) 
                            ?? new System.Collections.Generic.List<string>()
                    );
            });

            base.OnModelCreating(modelBuilder);
        }
    }

    public static class DbInitializer
    {
        public static void Initialize(MonitorDbContext context)
        {
            context.Database.EnsureCreated();
            
            // Add any seed data here if needed
        }
    }
}
