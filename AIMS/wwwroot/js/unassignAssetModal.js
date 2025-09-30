function debounce(func, delay) {
    let timeoutId;
    return function (...args) {
        clearTimeout(timeoutId); // Clear any existing timeout
        timeoutId = setTimeout(() => {
            func.apply(this, args); // Execute the original function after the delay
        }, delay);
    };
}



document.addEventListener("DOMContentLoaded", function () {
    // --- Modal and DOM Elements ---
    const unassignBtn = document.getElementById("unassign-asset-button");
    const modal = new bootstrap.Modal(document.getElementById("unassignAssetModal"));
    const userSelect = document.getElementById("userSelect");
    const assetSelect2 = document.getElementById("assetSelect2");
    let userPreviousOptionLength = userSelect2.options.length;
    let assetPreviousOptionLength = assetSelect2.options.length;

    // --- Create Dynamic Search Box Above Dropdown ---
    const searchInput = document.createElement("input");
    searchInput.type = "text";
    searchInput.placeholder = "Search users...";
    searchInput.className = "form-control mb-2";
    userSelect.parentElement.insertBefore(searchInput, userSelect);


    const assetInput = document.createElement("input");
    assetInput.type = "text";
    assetInput.placeholder = "Search assets...";
    assetInput.className = "form-control mb-2";
    assetSelect2.parentElement.insertBefore(assetInput, assetSelect2);


    // --- Open Dropdown on Focus ---
    searchInput.addEventListener("focus", () => {
        populateUserDropdown();
        userPreviousOptionLength = userSelect.options.length;
        userSelect.size = Math.min(userSelect.options.length, 11);
        userSelect.style.display = "block";
    });

    // --- Select First Matching Option on Enter ---
    searchInput.addEventListener("keydown", function (e) {
        if (e.key === "Enter") {
            e.preventDefault();
            const firstSelectable = Array.from(userSelect.options).find(opt => !opt.disabled);
            if (firstSelectable) {
                firstSelectable.selected = true;
                searchInput.value = "";
                userSelect.size = 1;
            }
        }
    });

    // --- Collapse Dropdown on Selection ---
    userSelect.addEventListener("change", function () {
        userSelect.size = 1;
        searchInput.value = "";
    });

    // --- Repopulate on Focus if Filtered Previously ---
    userSelect.addEventListener("focus", function () {
        if (searchInput.value === "" && userSelect.options.length < userPreviousOptionLength) {
            populateUserDropdown();
        }
    });

    // --- Close Dropdown if Clicking Outside ---
    document.addEventListener("click", (e) => {
        if (!searchInput.contains(e.target) && !userSelect.contains(e.target)) {
            userSelect.size = 1;
        }

        if (!assetInput.contains(e.target) && !assetSelect2.contains(e.target)) {
            assetSelect2.size = 1;
        }
    });

    function populateUserDropdown(searchTerm = "") {

        // fetch data
        fetch(`/api/user?searchString=${encodeURIComponent(searchTerm)}`)
            .then((response) => {
                return response.json();
            })
            .then((results) => {

                //clear out the old children
                userSelect.replaceChildren();

                results.forEach(user => {
                    const option = document.createElement("option");
                    option.value = user.fullName;
                    option.text = user.fullName;
                    option.dataset.user_id = user.userID;
                    userSelect.appendChild(option);
                });
            })
    }

    // --- Open Dropdown on Focus ---
    assetInput.addEventListener("focus", () => {
        populateAssetDropdown();
        assetPreviousOptionLength = assetSelect2.options.length;
        assetSelect2.size = Math.min(assetSelect2.options.length, 11);
        assetSelect2.style.display = "block";
    });

    // --- Select First Matching Option on Enter ---
    assetInput.addEventListener("keydown", function (e) {
        if (e.key === "Enter") {
            e.preventDefault();
            const firstSelectable = Array.from(assetSelect2.options).find(opt => !opt.disabled);
            if (firstSelectable) {
                firstSelectable.selected = true;
                assetInput.value = "";
                assetSelect2.size = 1;
            }
        }
    });

    // --- Collapse Dropdown on Selection ---
    assetSelect2.addEventListener("change", function () {
        assetSelect2.size = 1;
        assetInput.value = "";
    });

    // --- Repopulate on Focus if Filtered Previously ---
    assetSelect2.addEventListener("focus", function () {
        if (assetInput.value === "" && assetSelect2.options.length < assetPreviousOptionLength) {
            populateAssetDropdown();
        }
    });
    function populateAssetDropdown(searchTerm = "") {
        const selectedUserOption = userSelect.options[userSelect.selectedIndex];
        if (!selectedUserOption) {
            assetSelect2.replaceChildren();
            return;
        }

        const userId = selectedUserOption.dataset.user_id;

        fetch(`/api/assign/list?status=active`)
            .then(res => res.json())
            .then(assignments => {
                // Filter by this user
                const results = assignments.filter(a =>
                    a.userID == userId &&
                    (a.assetName.toLowerCase().includes(searchTerm.toLowerCase()))
                );

                assetSelect2.replaceChildren();

                results.forEach(a => {
                    const option = document.createElement("option");
                    option.value = `(${a.assignmentID}) ${a.assetName}`;
                    option.text = `(${a.assignmentID}) ${a.assetName}`;
                    option.dataset.asset_id = a.assetTag || a.softwareID;
                    option.dataset.asset_kind = a.assetKind;
                    option.dataset.assignment_id = a.assignmentID; // ✅ critical for unassign
                    assetSelect2.appendChild(option);
                });
            })
            .catch(err => {
                console.error("Failed to load assets for unassign", err);
            });
    }


    // --- Open Modal & Prepare Fields ---
    unassignBtn.addEventListener("click", function () {
        populateUserDropdown();
        populateAssetDropdown();
        document.getElementById("commentBox").value = "";
        modal.show();
    });

    // --- Filter Users on Typing ---
    searchInput.addEventListener("input", function () {
        populateUserDropdown(this.value);
    });
    // --- Filter Users on Typing ---
    assetInput.addEventListener("input", function () {
        populateAssetDropdown(this.value);
    });


    // --- Submit Unassignment Logic ---
    document.getElementById("unassignAssetForm")?.addEventListener("submit", async function (e) {
        e.preventDefault();

        const selectedAssetOption = assetSelect2.options[assetSelect2.selectedIndex];
        if (!selectedAssetOption) {
            alert("Please select an asset to unassign.");
            return;
        }

        const assignmentID = selectedAssetOption.dataset.assignment_id; // ✅ comes from populateAssetDropdown
        if (!assignmentID) {
            alert("No assignment ID found for the selected asset.");
            return;
        }

        try {
            const response = await fetch(`/api/assign/close?AssignmentID=${assignmentID}`, {
                method: 'POST'
            });

            if (!response.ok) {
                const errorBody = await response.text();
                throw new Error(`Unassignment Failed: ${errorBody}`);
            }

            // success toast
            const unassignToast = new bootstrap.Toast(document.getElementById("unassignToast"), { delay: 3000 });
            unassignToast.show();

        } catch (error) {
            const toastElement = document.getElementById("errorToast");
            const errorToast = new bootstrap.Toast(toastElement, { delay: 3000 });
            const messageBody = toastElement.querySelector('.toast-body');
            messageBody.innerHTML = error.message || error;
            errorToast.show();
        }

        modal.hide();
    });

    // // --- Submit Unassignment Logic ---
    // document.getElementById("unassignAssetForm")?.addEventListener("submit", function (e) {
    //     e.preventDefault();

    //     const selectedUserOption = userSelect.options[userSelect.selectedIndex];
    //     const selectedAssetOption = assetSelect2.options[assetSelect2.selectedIndex];

    //     const selectedUserID = selectedUserOption.dataset.user_id;
    //     const selectedAssetID = selectedAssetOption.dataset.asset_id;
    //     const selectedAssetKind = selectedAssetOption.dataset.asset_kind;

    //     async function getAssignmentId(userId, assetId) {
    //         const res = await fetch('/api/assign/list?status=active');
    //         const assignments = await res.json();

    //         // Find the matching assignment
    //         const match = assignments.find(a =>
    //             a.userID == userId &&
    //             (a.assetTag == assetId || a.softwareID == assetId)
    //         );

    //         return match ? match.assignmentID : null;
    //     }

        

    //     const url = '/api/assign/close'; // Replace with your API endpoint
    //     let data;

    //     // hardware
    //     if (selectedAssetKind == 1) {
    //         data = {
    //             "userID": selectedUserID,
    //             "assetKind": 1,
    //             "assetTag": selectedAssetID,
    //         }
    //         // software
    //     } else if (selectedAssetKind == 2) {
    //         data = {
    //             "userID": selectedUserID,
    //             "assetKind": 2,
    //             "softwareID": selectedAssetID,
    //         }
    //     }
    //     fetch(url, {
    //         method: 'POST', // Specify the HTTP method as POST
    //         headers: {
    //             'Content-Type': 'application/json'
    //         },
    //         body: JSON.stringify(data)
    //     })
    //         .then(response => {
    //             if (!response.ok) {
    //                 return response.text()
    //                     .then(errorBody => {
    //                         throw `Unassignment Failed: ${errorBody}`;
    //                     })
    //             }
    //             return response.json(); // Parse the JSON response
    //         })
    //         .then(result => {
    //             const unassignToast = new bootstrap.Toast(document.getElementById("unassignToast"), { delay: 3000 });
    //             unassignToast.show();
    //         })
    //         .catch(error => {
    //             const toastElement = document.getElementById("errorToast");
    //             const errorToast = new bootstrap.Toast(toastElement, { delay: 3000 });
    //             const messageBody = toastElement.querySelector('.toast-body');
    //             messageBody.innerHTML = error
    //             errorToast.show();
    //         });
    //     modal.hide();
    // });


});