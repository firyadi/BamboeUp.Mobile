using BamboeUp.Mobile.Modules.Services;

namespace BamboeUp.Mobile.Modules;

/// <summary>
/// SchemaStudio Mobile generators register module API clients here
/// (same pattern as BamboeUp.AppModules.DependencyInjection).
/// </summary>
public static class ModulesDependencyInjection
{
    public static IServiceCollection AddMobileModules(this IServiceCollection services)
    {
        services.AddScoped<OvertimeRequestApi>();
        services.AddScoped<HospitalApi>();
        services.AddScoped<WorkNoticeApi>();
        services.AddScoped<PersonApi>();
        services.AddScoped<EmployeeApi>();
        services.AddScoped<PayrollRunApi>();
        services.AddScoped<HolidayApi>();
        services.AddScoped<WorkItemApi>();
        // Generated registrations are appended below this marker.
        // <SCHEMASTUDIO_MOBILE_MODULES>
        services.AddScoped<AttendanceApi>();
        services.AddScoped<OvertimeRequestApi>();
        // </SCHEMASTUDIO_MOBILE_MODULES>
        return services;
    }
}
