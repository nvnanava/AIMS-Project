document.addEventListener("DOMContentLoaded", function () {
    // --- Modal and DOM Elements ---
    const assignBtn = document.getElementById("assign-asset-button");
    const modal = new bootstrap.Modal(document.getElementById("assignAssetModal"));
    const userSelect = document.getElementById("users");
    const assetSelect = document.getElementById("assetSelect");

    const searchInput = document.querySelector("#user-select")


    searchInput.addEventListener('input', (e) => {
        populateUserDropdown(e.target.value);
    })
    function populateUserDropdown(searchTerm ="") {
        //clear out the old children
        userSelect.replaceChildren();
        // fetch data
        fetch(`/api/user/search?searchString=${encodeURIComponent(searchTerm)}`)
                .then((response) => {
                    return response.json();
                })
                .then((results) => {
                    const seen = {}; // Object to track unique userIDs
                    // make sure we're not duplicating data
                    
                    results.forEach(user => {
                        if (!seen[user.userID]) { // Check if userID is already added
                    seen[user.userID] = true; // Mark userID as seen
                    }
                    const option = document.createElement("option");
                    option.value = user.fullName;
                    userSelect.appendChild(option);
                });
    })
}
    // // --- Asset Dropdown Logic (Filter for Available Only) ---
    // function populateAssetDropdown() {
    //     assetSelect.innerHTML = '<option disabled selected value="">Choose an asset...</option>';
    //     tableData
    //         .filter(a => a.Status === "Available")
    //         .forEach(asset => {
    //             const option = document.createElement("option");
    //             option.value = asset["Tag #"];
    //             option.textContent = `${asset["Asset Name"]} - ${asset["Tag #"]}`;
    //             assetSelect.appendChild(option);
    //         });
    // }

    // --- Open Modal & PRepare Fields ---
    assignBtn.addEventListener("click", function () {
        populateUserDropdown();
        // populateAssetDropdown();
        document.getElementById("commentBox").value = "";
        modal.show();
    });



    // --- Submit Assignment Logic ---
    document.getElementById("assignAssetForm")?.addEventListener("submit", function (e) {
        e.preventDefault();

        const selectedUserID = userSelect.value;
        const selectedAssetTag = assetSelect.value;

        const selectedUser = users.find(u => u.ID === selectedUserID);
        const selectedAsset = tableData.find(a => a["Tag #"] === selectedAssetTag);

        const comment = document.getElementById("commentBox").value.trim();

        if (selectedUser && selectedAsset) {

            // Update dummy dataset
            selectedAsset["Assigned To"] = `${selectedUser.Name} (${selectedUserID})`;
            selectedAsset["Status"] = "Assigned";

            // Update visible UI
            const rows = document.querySelectorAll("#table-body tr");
            rows.forEach(row => {
                const tagCell = row.querySelector("td:nth-child(3)");
                if (tagCell && tagCell.textContent.trim() === selectedAssetTag) {
                    const assignedToCell = row.querySelector("td:nth-child(4)");
                    const statusCell = row.querySelector("td:nth-child(5)");
                    const existingSpan = statusCell.querySelector("span");

                    if (assignedToCell) assignedToCell.textContent = `${selectedUser.Name} (${selectedUser.ID})`;
                    if (existingSpan) {
                        existingSpan.className = "status assigned";
                        existingSpan.textContent = "Assigned";
                    }
                }
            });
            // TODO: Store comment in audit log table (future database integration)
            // TODO: When backend is connected, send assignment + comment to database.
        }
        modal.hide();
    });

    // --- Placeholder: User Agreement Upload Handler ---
    const fileInput = document.getElementById("userAgreementUpload");
    fileInput.addEventListener("change", function () {
        const selectedFile = fileInput.files[0];
        if (selectedFile) {
            // TODO: Upload file to SharePoint using Microsoft Graph API
            // TODO: Log file name + date + user in audit log table
        }
    });

    // --- Future Considerations ---
    // TODO: Consiter integrating with DocuSign or similar for dynamic agreement generation.
    // The form could autofill asset + user info and require a signature before assignment completes.
});
