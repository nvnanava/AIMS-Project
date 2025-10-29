/*
* Search Intercept Component
* Listens for search events and can modify or block them depending on whether user is already on search page.

Purpose
- listens for search events
- If user not on search page...search as usual
- If user is on search page...intercept event and reload using client side cache to avoid hitting the backend again.
- Uses aimsFetch utility to leverage client-side caching and deduplication.

Added to _Layout.cshtml: <script src="~/js/components/search-intercept.js" asp-append-version="true" defer></script> 

*/

document.addEventListener("DOMContentLoaded", function () {
    // Check if we are on the search page
    const form = document.querySelector('form[action*="/Search"]');
    const input = document.getElementById("search-input");  
    if (!form || !input) return; // Not on search page

    form.addEventListener("submit", async function (e) {
        const path = window.location.pathname.toLowerCase();
        const isOnSearchPage =
            path.endsWith("/search") || path.endsWith("/search/index");
        if (!isOnSearchPage) return; // Let normal search happen
        e.preventDefault(); // Intercept search
        const q = input.value.trim();
        if (!q) return; // Ignore empty searches

        if (!window.AIMS?.Search?.refresh) return;

        await window.AIMS.Search.refreshQuery(q);
    });
});