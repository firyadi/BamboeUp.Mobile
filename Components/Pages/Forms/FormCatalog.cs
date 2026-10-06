namespace BamboeUp.Mobile.Components.Pages.Forms;

public sealed record FormMenuItem(string Code, string Title, string Table, string Href, bool Ready);

public static class FormCatalog
{
    public static readonly IReadOnlyList<FormMenuItem> All =
    [
        new("Mobile-01", "1 Table", "WorkNotice", "/work-notices", true),
        new("Mobile-02", "Header-Detail", "Person", "/people", true),
        new("Mobile-03", "Wizard", "Person", "/people/onboard", true),
        new("Mobile-04", "Master Detail", "Employee", "/employees", true),
        new("Mobile-05", "Process", "PayrollRun", "/payroll-runs", true),
        new("Mobile-06", "Calendar", "Holiday", "/holidays", true),
        new("Mobile-07", "Kanban", "WorkItem", "/work-items", true),
        new("Mobile-08", "GPS Tracker", "Session and points", "/forms/mobile-08", false),
        new("Mobile-09", "Inspection Checklist", "No web table yet", "/forms/mobile-09", false),
        new("Mobile-11", "Physical Inventory Count", "Stock Opname", "/physical-inventory", true),
    ];

    public static FormMenuItem? Find(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;
        return All.FirstOrDefault(item =>
            item.Code.Equals(key, StringComparison.OrdinalIgnoreCase)
            || item.Href.EndsWith("/" + key, StringComparison.OrdinalIgnoreCase));
    }
}
