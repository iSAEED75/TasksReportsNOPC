using Microsoft.EntityFrameworkCore;
using TasksWebApp.Models;

namespace TasksWebApp.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.EnsureCreatedAsync();

        // افزودن امکانات جدید به دیتابیس موجود، بدون حذف اطلاعات قبلی.
        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'dbo.TaskLists', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.TaskLists (
                    Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_TaskLists PRIMARY KEY,
                    Name nvarchar(100) NOT NULL,
                    OwnerUserId int NOT NULL,
                    CreatedAt datetime2 NOT NULL CONSTRAINT DF_TaskLists_CreatedAt DEFAULT (SYSUTCDATETIME()),
                    CONSTRAINT FK_TaskLists_Users_OwnerUserId FOREIGN KEY (OwnerUserId) REFERENCES dbo.Users(Id) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX IX_TaskLists_OwnerUserId_Name ON dbo.TaskLists(OwnerUserId, Name);
            END;
            """);

        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'dbo.TaskItems', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.TaskItems', N'IsStarred') IS NULL
                ALTER TABLE dbo.TaskItems ADD IsStarred bit NOT NULL CONSTRAINT DF_TaskItems_IsStarred DEFAULT (0);
            IF OBJECT_ID(N'dbo.TaskItems', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.TaskItems', N'SortOrder') IS NULL
                ALTER TABLE dbo.TaskItems ADD SortOrder int NOT NULL CONSTRAINT DF_TaskItems_SortOrder DEFAULT (0);
            IF OBJECT_ID(N'dbo.TaskItems', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.TaskItems', N'RecurrenceType') IS NULL
                ALTER TABLE dbo.TaskItems ADD RecurrenceType int NOT NULL CONSTRAINT DF_TaskItems_RecurrenceType DEFAULT (0);
            IF OBJECT_ID(N'dbo.TaskItems', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.TaskItems', N'ReminderAt') IS NULL
                ALTER TABLE dbo.TaskItems ADD ReminderAt datetime2 NULL;
            IF OBJECT_ID(N'dbo.TaskItems', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.TaskItems', N'ParentTaskId') IS NULL
                ALTER TABLE dbo.TaskItems ADD ParentTaskId int NULL;
            IF OBJECT_ID(N'dbo.TaskItems', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.TaskItems', N'TaskListId') IS NULL
                ALTER TABLE dbo.TaskItems ADD TaskListId int NULL;
            """);

        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'dbo.TaskItems', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.TaskItems', N'ParentTaskId') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_TaskItems_TaskItems_ParentTaskId')
                ALTER TABLE dbo.TaskItems ADD CONSTRAINT FK_TaskItems_TaskItems_ParentTaskId
                    FOREIGN KEY (ParentTaskId) REFERENCES dbo.TaskItems(Id);
            IF OBJECT_ID(N'dbo.TaskItems', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.TaskItems', N'TaskListId') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_TaskItems_TaskLists_TaskListId')
                ALTER TABLE dbo.TaskItems ADD CONSTRAINT FK_TaskItems_TaskLists_TaskListId
                    FOREIGN KEY (TaskListId) REFERENCES dbo.TaskLists(Id) ON DELETE SET NULL;
            """);

        // افزودن ستون نوع ثبت به پایگاه داده‌های نسخه قبلی، بدون حذف اطلاعات.
        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'dbo.TaskItems', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.TaskItems', N'RecordType') IS NULL
            BEGIN
                ALTER TABLE dbo.TaskItems ADD RecordType int NOT NULL
                    CONSTRAINT DF_TaskItems_RecordType DEFAULT (0);
            END;
            """);


        // ارتقای پایگاه داده‌های قدیمی بدون حذف اطلاعات قبلی.
        // هر مرحله جداگانه اجرا می‌شود تا SQL Server ستون تازه‌اضافه‌شده را
        // پیش از اجرای دستور UPDATE شناسایی کند.
        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'dbo.TaskItems', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.TaskItems', N'WorkDate') IS NULL
            BEGIN
                ALTER TABLE dbo.TaskItems ADD WorkDate date NULL;
            END;
            """);

        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'dbo.TaskItems', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.TaskItems', N'WorkDate') IS NOT NULL
               AND COL_LENGTH(N'dbo.TaskItems', N'CreatedAt') IS NOT NULL
            BEGIN
                UPDATE dbo.TaskItems
                SET WorkDate = CONVERT(date, CreatedAt)
                WHERE WorkDate IS NULL;
            END;
            """);

        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'dbo.TaskItems', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.TaskItems', N'WorkDate') IS NOT NULL
               AND EXISTS (SELECT 1 FROM dbo.TaskItems WHERE WorkDate IS NULL)
            BEGIN
                UPDATE dbo.TaskItems SET WorkDate = CONVERT(date, GETDATE())
                WHERE WorkDate IS NULL;
            END;
            """);

        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'dbo.TaskItems', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.TaskItems', N'WorkDate') IS NOT NULL
               AND EXISTS (
                   SELECT 1 FROM sys.columns
                   WHERE object_id = OBJECT_ID(N'dbo.TaskItems')
                     AND name = N'WorkDate'
                     AND is_nullable = 1
               )
            BEGIN
                ALTER TABLE dbo.TaskItems ALTER COLUMN WorkDate date NOT NULL;
            END;
            """);

        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'dbo.TaskItems', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.TaskItems', N'DurationHours') IS NULL
            BEGIN
                ALTER TABLE dbo.TaskItems ADD DurationHours decimal(5,2) NOT NULL
                    CONSTRAINT DF_TaskItems_DurationHours DEFAULT (1.00);
            END;
            """);

        if (!await db.Users.AnyAsync())
        {
            db.Users.Add(new User
            {
                Username = "admin",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@1234"),
                FullName = "مدیر سیستم",
                EmployeeNumber = "0001",
                Role = "Admin",
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();

        var users = await db.Users.AsNoTracking().Select(x => x.Id).ToListAsync();
        foreach (var ownerId in users)
        {
            if (!await db.TaskLists.AnyAsync(x => x.OwnerUserId == ownerId))
                db.TaskLists.Add(new TaskList { OwnerUserId = ownerId, Name = "کارهای من", CreatedAt = DateTime.UtcNow });
        }
        await db.SaveChangesAsync();

        // تخصیص وظایف قدیمی فقط یک بار انجام می‌شود؛ فهرستی که کاربر بعداً حذف کند
        // در شروع‌های بعدی دوباره به‌طور ناخواسته روی وظایف قرار نمی‌گیرد.
        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'dbo.AppSchemaMigrations', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.AppSchemaMigrations (
                    MigrationKey nvarchar(100) NOT NULL CONSTRAINT PK_AppSchemaMigrations PRIMARY KEY,
                    AppliedAt datetime2 NOT NULL CONSTRAINT DF_AppSchemaMigrations_AppliedAt DEFAULT (SYSUTCDATETIME())
                );
            END;
            """);
        if (!await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM dbo.AppSchemaMigrations WHERE MigrationKey = N'AssignExistingTasksToDefaultList_v1'").AnyAsync())
        {
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE t SET TaskListId = l.Id
                FROM dbo.TaskItems t
                INNER JOIN dbo.TaskLists l ON l.OwnerUserId = t.CreatedById AND l.Name = N'کارهای من'
                WHERE t.TaskListId IS NULL;
                """);
            await db.Database.ExecuteSqlRawAsync("INSERT INTO dbo.AppSchemaMigrations (MigrationKey) VALUES (N'AssignExistingTasksToDefaultList_v1');");
        }

        string[] names =
        {
            "فعالیت‌های روزانه", "گزارش‌نویسی", "آموزش",
            "حراست و نگهبانی", "امور اداری", "جلسات", "پیگیری و هماهنگی"
        };

        foreach (var name in names)
        {
            if (!await db.Categories.AnyAsync(c => c.Name == name))
                db.Categories.Add(new Category { Name = name });
        }

        await db.SaveChangesAsync();
    }
}
