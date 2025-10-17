namespace AIMS.Routing
{
    public sealed class AllowedAssetTypeConstraint : IRouteConstraint
    {
        // Map slugs (and plurals) -> canonical category string our app uses
        private static readonly Dictionary<string, string> _slugToCategory =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                // singulars
                ["laptop"] = "Laptop",
                ["desktop"] = "Desktop",
                ["monitor"] = "Monitor",
                ["software"] = "Software",
                ["headset"] = "Headset",
                ["charging-cable"] = "Charging Cable",

                // plurals (accepted and normalized to singular)
                ["laptops"] = "Laptop",
                ["desktops"] = "Desktop",
                ["monitors"] = "Monitor",
                ["headsets"] = "Headset",
                ["softwares"] = "Software",       // just in case
                ["charging-cables"] = "Charging Cable",
            };

        public static bool TryNormalize(string? slug, out string category)
        {
            category = "";
            if (string.IsNullOrWhiteSpace(slug)) return false;

            // Normalize spaces to hyphens to be forgiving: "charging cable" → "charging-cable"
            var key = slug.Trim().Replace(' ', '-');

            if (_slugToCategory.TryGetValue(key, out var cat))
            {
                category = cat;
                return true;
            }
            return false;
        }

        public bool Match(HttpContext? httpContext, IRouter? route, string routeKey,
                          RouteValueDictionary values, RouteDirection routeDirection)
        {
            if (!values.TryGetValue(routeKey, out var obj) || obj is null) return false;
            return TryNormalize(obj.ToString(), out _);
        }
    }
}
