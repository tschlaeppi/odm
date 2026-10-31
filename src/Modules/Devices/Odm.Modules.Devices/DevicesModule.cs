using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Odm.Modules.Devices.Domain;
using Odm.Modules.Devices.Endpoints;
using Odm.Modules.Devices.Infrastructure;
using Odm.SharedKernel;
using Odm.SharedKernel.Contracts;
using Odm.SharedKernel.Persistence;

namespace Odm.Modules.Devices;

public sealed class DevicesModule : IModule
{
    public string Name => "Devices";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IDeviceRegistry, DeviceRegistry>();
        services.AddScoped<IDeviceDirectory, DeviceDirectory>();
        services.AddScoped<IDeviceInventory, DeviceInventoryStore>();
        services.AddScoped<IAlertSource, DeviceAlertSource>();
        services.AddDbContext<DevicesDbContext>((sp, o) =>
            o.UseOdmDatabase(sp.GetRequiredService<IOptions<DatabaseOptions>>().Value, DevicesDbContext.Schema));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        DeviceEndpoints.Map(endpoints);
        GroupEndpoints.Map(endpoints);
        InventoryEndpoints.Map(endpoints);
    }

    public async Task InitializeAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DevicesDbContext>();
        await db.MigrateOdmAsync(ct);

        var seed = scope.ServiceProvider.GetRequiredService<IConfiguration>().GetValue<bool>("Devices:SeedDemoData");
        if (!seed)
        {
            return;
        }

        if (!await db.Devices.AnyAsync(ct))
        {
            db.Devices.AddRange(
                Demo("PC-0001", OsFamily.Windows, "Windows 11 Pro", "24H2", "anna.muster", "Finanz", "KST-1000"),
                Demo("PC-0002", OsFamily.Windows, "Windows 11 Pro", "23H2", "ben.beispiel", "Einkauf", "KST-1000"),
                Demo("PC-0003", OsFamily.Windows, "Windows 10 Enterprise", "22H2", "dora.demo", "Finanz", "KST-1100"),
                Demo("SRV-WIN-01", OsFamily.Windows, "Windows Server 2022", "21H2", null, null, "KST-9000"),
                Demo("lx-dev-01", OsFamily.Linux, "Ubuntu", "24.04", "carla.test", "IT", "KST-2000"),
                Demo("lx-srv-01", OsFamily.Linux, "Red Hat Enterprise Linux", "9.4", null, null, "KST-9000"));
            await db.SaveChangesAsync(ct);
        }

        if (!await db.Groups.AnyAsync(ct))
        {
            db.Groups.AddRange(
                DeviceGroup.Create("Windows 11 Clients", "All Windows 11 workstations", GroupMatchMode.All,
                [
                    GroupRule.Create(GroupField.OsFamily, MatchOperator.Is, "Windows"),
                    GroupRule.Create(GroupField.OsName, MatchOperator.Contains, "Windows 11")
                ]),
                DeviceGroup.Create("Server", "Name starts with SRV or contains -srv-", GroupMatchMode.Any,
                [
                    GroupRule.Create(GroupField.Name, MatchOperator.StartsWith, "SRV"),
                    GroupRule.Create(GroupField.Name, MatchOperator.Contains, "-srv-")
                ]),
                DeviceGroup.Create("Finanz", "Hauptbenutzer in der OU Finanz", GroupMatchMode.All,
                [
                    GroupRule.Create(GroupField.OrgUnit, MatchOperator.Is, "Finanz")
                ]));
            await db.SaveChangesAsync(ct);
        }
    }

    private static Device Demo(
        string name, OsFamily family, string os, string version, string? user, string? orgUnit, string costCenter)
    {
        var device = Device.Register(name, family, os, version);
        device.UpdateAssignment(user, orgUnit, costCenter);
        return device;
    }
}
