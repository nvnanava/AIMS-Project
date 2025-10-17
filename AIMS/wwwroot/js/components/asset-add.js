document.addEventListener('DOMContentLoaded', function () {

    const assetForm = document.getElementById('AssetAddForm'); //phase 1 form
    const addAssetModal = document.getElementById('addAssetModal');
    const assetFormError = document.getElementById('assetFormError');
    const errorBox = document.getElementById('serverErrorMessage');

    const itemForm = document.getElementById('ItemDetailsForm'); //phase 2 form
    const itemDetailsModal = document.getElementById('itemDetailsModal');
    const itemStep = document.getElementById('itemStep');
    const previewList = document.getElementById('previewList');
    const nextItemBtn = document.getElementById('nextItemBtn');
    const submitAllBtn = document.getElementById('submitAllBtn');

    //beginning state with 0 items added
    let baseData = {};
    let itemCount = 0;
    let currentIndex = 0; // how many *slots* we have advanced through (1..itemCount)
    let items = [];
    let inTransition = false;
    let editIndex = null; // which item is being edited, null if none

    // ---------------- helpers ----------------

    //load next item helper func
    function loadNextItem() {
        itemForm.reset();
        currentIndex = items.length + 1; // next slot is 1-based
        itemStep.textContent = `Item ${currentIndex} of ${itemCount}`;
        nextItemBtn.style.display = "inline-block";
        submitAllBtn.style.display = "none";
        document.getElementById('itemInputs').style.display = "block";
    }

    //helper to set error state on input
    function setError(id, message) {
        const input = document.getElementById(id);
        input.classList.add("is-invalid");
        const errorElem = document.getElementById(id + "Error");
        if (errorElem) errorElem.textContent = message;
    }
    //helper to clear error state on input
    function clearError(id) {
        const input = document.getElementById(id);
        input.classList.remove("is-invalid");
        const errorElem = document.getElementById(id + "Error");
        if (errorElem) errorElem.textContent = "";
    }

    /*
      Save/load progress functions. Only saving data locally in browser for now. We can discuss saving in database with client.
    */
    function saveProgress() {
        const data = {
            baseData,
            itemCount,               // use variable, not DOM lookup
            currentIndex: items.length,
            items
        };
        localStorage.setItem('assetProgress', JSON.stringify(data));
        alert('Progress saved locally in browser.');
    }

    function loadProgress() {
        const save = localStorage.getItem('assetProgress');
        if (!save) {
            alert("No saved progress found.");
            return;
        }

        try {
            //if we can find the saved data load that. If not we will just use defaults.
            const data = JSON.parse(save);
            if (data.baseData) baseData = data.baseData;
            if (data.itemCount) itemCount = data.itemCount;
            if (Array.isArray(data.items)) items = data.items;

            //rebuilding the preview list
            previewList.innerHTML = "";
            items.forEach(i => {
                const li = document.createElement("li");
                li.classList.add("list-group-item");
                li.textContent = `${i.SerialNumber} | ${i.AssetTag}`;
                previewList.appendChild(li);
            });

            //if we have already added some items, go to phase 2 directly
            if (items.length >= itemCount) {
                // all items are already captured
                itemStep.textContent = `All ${itemCount} items entered. Review and submit`;
                document.getElementById('itemInputs').style.display = "none"; // hide the input boxes
                nextItemBtn.style.display = "none";
                submitAllBtn.style.display = "inline-block";
            } else {
                loadNextItem(); // shows the next slot number based on items.length
            }
        } catch (e) {
            console.error("Error loading saved progress:", e);
        }
    }

    function clearSaveProgress() {
        localStorage.removeItem("assetProgress");
    }

    //reset modal state of phase 1 form
    addAssetModal.addEventListener('hidden.bs.modal', function () {
        if (!inTransition) {
            assetForm.reset();
            assetFormError.style.display = "none";
            assetFormError.textContent = "";
            errorBox.style.display = "none";
            errorBox.textContent = "";
            assetForm.querySelectorAll('.is-invalid').forEach(el => el.classList.remove('is-invalid'));
            //use clear error on all fields
            ['manufacturer', 'model', 'itemCount', 'addPurchaseDate', 'warrantyExpiration'].forEach(clearError);
        }
        inTransition = false;
    });

    //reset modal state of phase 2 form
    itemDetailsModal.addEventListener('hidden.bs.modal', function () {
        itemForm.reset();
        // reset UI so fields are visible again
        document.getElementById('itemInputs').style.display = "block";
        nextItemBtn.style.display = "inline-block";
        submitAllBtn.style.display = "none";
        itemStep.textContent = "";
        previewList.innerHTML = "";
        clearError('serialNumber');
        clearError('tagNumber');
    });

    addAssetModal.addEventListener('show.bs.modal', function () {
        const saved = localStorage.getItem("assetProgress");
        if (saved) {
            if (!confirm("You have saved progress. Opening this modal will clear it. Continue?")) {
                inTransition = true; //marks that we are transitioning modals to avoid reset
                bootstrap.Modal.getInstance(addAssetModal).hide();
                inTransition = false;
                return;
            }
            clearSaveProgress();
        }
        baseData = {};
        itemCount = 0;
        currentIndex = 0;
        items = [];
        previewList.innerHTML = "";
    });

    // ---------------- Phase 1 - collect base asset data and number of items ----------------
    document.getElementById('startPhase2btn').addEventListener('click', function (e) {
        e.preventDefault();
        const assetFormError = document.getElementById('assetFormError');
        assetFormError.style.display = "none";
        assetFormError.textContent = "";

        // Validate required fields
        const requiredFields = [
            { id: "assetType", message: "Asset Type is required" },
            { id: "manufacturer", message: "Manufacturer is required", errorId: "manufacturerError" },
            { id: "model", message: "Model is required", errorId: "modelError" },
            { id: "itemCount", message: "Please enter number of items", errorId: "itemCountError" },
            //{ id: "addPurchaseDate", message: "Purchase Date is required", errorId: "purchaseDateError" },
            //{ id: "warrantyExpiration", message: "Warranty Expiration Date is required", errorId: "warrantyExpirationError" },
        ];

        let valid = true;

        //loop through required fields and show error if any are missing.
        requiredFields.forEach(field => {
            const input = document.getElementById(field.id);
            if (!input.value.trim()) {
                setError(field.id, field.message); //using helper
                valid = false;
            } else {
                clearError(field.id); //using helper to clear error state
            }
        });

        const purchaseDateInput = document.getElementById('addPurchaseDate');
        const purchaseDate = new Date(purchaseDateInput.value);
        const today = new Date();
        today.setHours(0, 0, 0, 0); // normalize to midnight

        if (!purchaseDateInput.value.trim()) {
            setError("addPurchaseDate", "Purchase Date is required");
            valid = false;
        } else if (purchaseDate > today) {
            setError("addPurchaseDate", "Purchase Date cannot be in the future");
            valid = false;
        } else {
            clearError("addPurchaseDate");
        }

        //ensure user input of warranty expiration date is not before purchase date
        const warrantyInput = document.getElementById('warrantyExpiration');
        const warrantyDateValue = warrantyInput.value;
        const warrantyDate = new Date(warrantyDateValue);

        if (warrantyDateValue && purchaseDateInput.value.trim()) {
            if (warrantyDate < purchaseDate) {
                setError("warrantyExpiration", "Warranty Expiration Date cannot be before Purchase Date");
                valid = false;
            } else {
                clearError("warrantyExpiration");
            }
        } else {
            clearError("warrantyExpiration");
        }

        //item count must be a positive integer
        itemCount = parseInt(document.getElementById('itemCount').value, 10);
        if (isNaN(itemCount) || itemCount <= 0) {
            setError("itemCount", "Must be at least 1");
            valid = false;
        } else {
            clearError("itemCount");
        }

        if (!valid) return;

        //Save base asset data
        baseData = {
            AssetType: document.getElementById('assetType').value.trim(),
            Manufacturer: document.getElementById('manufacturer').value.trim(),
            Model: document.getElementById('model').value.trim(),
            PurchaseDate: document.getElementById('addPurchaseDate').value.trim(),     // yyyy-MM-dd
            WarrantyExpiration: document.getElementById('warrantyExpiration').value.trim(), // yyyy-MM-dd
            Status: "Available"
        };

        // reset item state
        currentIndex = 0;
        items = [];
        previewList.innerHTML = "";

        //close modal 1 and start phase 2
        inTransition = true; //marks that we are transitioning modals to avoid reset
        bootstrap.Modal.getInstance(addAssetModal).hide();
        new bootstrap.Modal(itemDetailsModal).show();
        inTransition = false;
        loadNextItem();
    });

    // ---------------- Phase 2 - per-item (tags/serials) ----------------

    // Add the current inputs as an item (with validations). Returns true if added.
    function addCurrentInputsAsItem() {
        const serial = document.getElementById('serialNumber').value.trim();
        const tag = document.getElementById('tagNumber').value.trim();

        clearError('serialNumber'); //clear errors first
        clearError('tagNumber');

        let valid = true;
        if (!serial) {
            setError('serialNumber', 'Serial Number is required.');
            valid = false;
        }
        if (!tag) {
            setError('tagNumber', 'Tag Number is required.');
            valid = false;
        }

        if (!valid) return false;

        //check for duplicates in the current batch
        if (items.some(i => i.SerialNumber === serial)) {
            setError('serialNumber', 'Duplicate serial number in this batch.');
            return false;
        }
        if (items.some(i => i.AssetTag === tag)) {
            setError('tagNumber', 'Duplicate tag number in this batch.');
            return false;
        }

        //after validation, add to items array
        items.push({
            ...baseData,
            AssetName: `${baseData.Manufacturer} ${baseData.Model}`.trim(), //concat make/model for name
            SerialNumber: serial,
            AssetTag: tag
        });

        //update preview list
        renderPreviewList();

        return true;
    }

    //Phase 2 - enter in tags/serial#
    nextItemBtn.addEventListener('click', function (e) {
        e.preventDefault();

        if (items.length >= itemCount) {
            // Already reached the desired count
            itemStep.textContent = `All ${itemCount} items entered. Review and submit`;
            document.getElementById('itemInputs').style.display = "none";
            nextItemBtn.style.display = "none";
            submitAllBtn.style.display = "inline-block";
            return;
        }

        if (!addCurrentInputsAsItem()) return;

        if (items.length >= itemCount) {
            //if we just entered the last item, hide next button and show submit button
            itemStep.textContent = `All ${itemCount} items entered. Review and submit`;
            document.getElementById('itemInputs').style.display = "none";
            nextItemBtn.style.display = "none";
            submitAllBtn.style.display = "inline-block";
        } else {
            loadNextItem();
        }
    });

    //save progress button
    document.getElementById('saveProgress').addEventListener('click', saveProgress);

    //load progress button in asset manage menu
    document.getElementById('loadProgressBtn').addEventListener('click', function (e) {
        e.preventDefault();

        // try to load from localStorage
        const saved = localStorage.getItem("assetProgress");
        if (!saved) {
            alert("No saved progress found.");
            return;
        }

        const addAssetModal = bootstrap.Modal.getInstance(document.getElementById('addAssetModal'));
        if (addAssetModal) addAssetModal.hide();

        //show phase 2 modal and load saved data
        const itemModal = new bootstrap.Modal(document.getElementById('itemDetailsModal'));
        itemModal.show();
        loadProgress();
    });

    // ---------------- Submit all items to server ----------------
    submitAllBtn.addEventListener('click', async function (e) {
        e.preventDefault();

        // If the user typed the last row but didn’t click “Next”, fold it in (only if we still need more)
        if (items.length < itemCount) {
            const serial = document.getElementById('serialNumber').value.trim();
            const tag = document.getElementById('tagNumber').value.trim();
            if (serial && tag) {
                const added = addCurrentInputsAsItem();
                if (!added) return; // show input errors if any
            }
        }

        // Guard: if we still didn't reach itemCount, stop and prompt
        if (items.length < itemCount) {
            setError('serialNumber', 'Please add all items before submitting.');
            return;
        }

        // Build payload from the items collected
        const cleaned = items.map(r => ({
            AssetTag: (r.AssetTag ?? "").trim(),
            Manufacturer: (r.Manufacturer ?? "").trim(),
            Model: (r.Model ?? "").trim(),
            SerialNumber: (r.SerialNumber ?? "").trim(),
            AssetType: (r.AssetType ?? "").trim(),
            Status: (r.Status ?? "").trim(),
            // IMPORTANT: keep dates as "yyyy-MM-dd" (what <input type="date"> provides)
            PurchaseDate: r.PurchaseDate,            // "yyyy-MM-dd"
            WarrantyExpiration: r.WarrantyExpiration // "yyyy-MM-dd"
        }));

        try {
            const res = await fetch("/api/hardware/add-bulk", {
                method: "POST",
                headers: { "Content-Type": "application/json", "Accept": "application/json" },
                body: JSON.stringify({ dtos: cleaned }) // <-- wrapper required
            });

            if (res.ok) {
                // clear saved draft on success
                clearSaveProgress();

                // close modal
                const modal = bootstrap.Modal.getInstance(itemDetailsModal);
                if (modal) modal.hide();

                // small pause to let the modal finish animating
                await new Promise(r => setTimeout(r, 250));

                // If a page-level refresh function exists, use it; otherwise reload.
                if (typeof window.loadAssetsPaged === 'function') {
                    try {
                        await window.loadAssetsPaged(baseData.AssetType, 1, 50);
                    } catch (e) {
                        window.location.reload();
                    }
                } else {
                    window.location.reload();
                }
                return;
            }

            // Robust error parse (handles ValidationProblem and plain text)
            let data;
            try { data = await res.json(); }
            catch {
                data = { title: `HTTP ${res.status}`, detail: await res.text() };
            }
            showServerErrorsInline(data);
        } catch (err) {
            showErrorMessages({ title: err.name || "Client error", detail: err.message || "Unexpected error" }, errorBox);
        }
    });

    function renderPreviewList() {
        previewList.innerHTML = "";
        items.forEach((item, index) => {
            const li = document.createElement("li");
            li.className = "list-group-item d-flex justify-content-between align-items-center";
            li.dataset.index = index;

            const span = document.createElement("span");
            span.textContent = `${item.SerialNumber} | ${item.AssetTag}`;
            li.appendChild(span);

            // Edit button
            const editBtn = document.createElement("button");
            editBtn.type = "button";
            editBtn.className = "action-btn blue-pencil";
            editBtn.innerHTML = `
            <svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" fill="#032447ff" class="bi bi-pencil" viewBox="0 0 16 16">
                <path d="M12.146.854a.5.5 0 0 1 .708 0l2.292 2.292a.5.5 0 0 1 0 .708l-10 10a.5.5 0 0 1-.168.11l-4 1a.5.5 0 0 1-.62-.62l1-4a.5.5 0 0 1 .11-.168l10-10zM11.207 2.5 13.5 4.793 12.5 5.793 10.207 3.5l1-1zm1.586 1.586-1-1L3 11.293V12h.707L12.793 4.086z"/>
            </svg>
        `;
            editBtn.addEventListener("click", () => startEditItem(index));
            li.appendChild(editBtn);

            previewList.appendChild(li);
        });
    }

    // Start editing an existing item
    function startEditItem(index) {
        editIndex = index;
        //on click of edit button, load item data into form
        const item = items[index];
        document.getElementById('editSerialNumber').value = item.SerialNumber;
        document.getElementById('editTagNumber').value = item.AssetTag;
        //show slim modal

        const editModal = new bootstrap.Modal(document.getElementById('editItemModal'));
        editModal.show();

    }
    document.getElementById('saveEditBtn').addEventListener("click", function () {
        if (editIndex !== null) {
            items[editIndex].SerialNumber = document.getElementById('editSerialNumber').value.trim();
            items[editIndex].AssetTag = document.getElementById('editTagNumber').value.trim();
            renderPreviewList();

            // Close modal
            const editModalEl = document.getElementById('editItemModal');
            const modalInstance = bootstrap.Modal.getInstance(editModalEl);
            modalInstance.hide();

            editIndex = null;
        }
    });

    function showServerErrorsInline(data) { //different from Software inline error message with the way the payloads are currently returned.
        const list = document.getElementById("previewList");
        if (!list) return;

        // Clear old errors
        list.querySelectorAll("li").forEach(li => {
            li.classList.remove("list-group-item-danger");
            const existing = li.querySelector(".inline-error");
            if (existing) existing.remove();
        });

        const errors = data?.errors || data;
        if (!errors) return;

        let anyShown = false;

        for (const key in errors) {
            const messages = Array.isArray(errors[key]) ? errors[key] : [errors[key]];

            // get numeric index from keys like "Dtos[0]" or "[0]"
            const match = key.match(/\[(\d+)\]/);
            const index = match ? parseInt(match[1], 10) : NaN;

            if (!isNaN(index) && list.children[index]) {
                const li = list.children[index];
                li.classList.add("list-group-item-danger");
                const div = document.createElement("div");
                div.className = "inline-error text-danger small mt-1";
                div.textContent = messages.join(", ");
                li.appendChild(div);

                li.scrollIntoView({ behavior: "smooth", block: "center" });
                anyShown = true;
            }
        }

        // Fallback if no inline match (e.g., no [index] found)
        if (!anyShown) {
            const modal = new bootstrap.Modal(document.getElementById("serverErrorModal"));
            const listEl = document.getElementById("serverErrorList");
            listEl.innerHTML = "";
            for (const key in errors) {
                const li = document.createElement("li");
                li.className = "list-group-item text-danger";
                li.textContent = `${key}: ${Array.isArray(errors[key]) ? errors[key].join(", ") : errors[key]}`;
                listEl.appendChild(li);
            }
            modal.show();
        }
    }

    //helper to show error messages in the server error modal
    function showServerErrors(data) {
        const errorList = document.getElementById("serverErrorList");
        errorList.innerHTML = "";

        const messages = [];

        try {
            if (data && typeof data === "object") {
                // Standard ASP.NET Core ValidationProblem payload
                if (data.errors && typeof data.errors === "object") {
                    for (const [key, arr] of Object.entries(data.errors)) {
                        if (Array.isArray(arr)) {
                            for (const m of arr) messages.push(m);
                        } else if (typeof arr === "string") {
                            messages.push(arr);
                        }
                    }
                }

                // If nothing yet, also surface title/detail if present
                if (data.title) messages.push(String(data.title));
                if (data.detail) messages.push(String(data.detail));

                // Some APIs may return { error: "..." }
                if (data.error && typeof data.error === "string") {
                    messages.push(data.error);
                }

                // Fallback: collect top-level array-of-strings props
                if (messages.length === 0) {
                    for (const [k, v] of Object.entries(data)) {
                        if (Array.isArray(v)) {
                            for (const m of v) if (typeof m === "string") messages.push(m);
                        }
                    }
                }
            } else if (typeof data === "string") {
                messages.push(data);
            }
        } catch (e) {
            console.warn("Error parsing server errors:", e);
        }

        if (messages.length === 0) {
            messages.push("An unknown error occurred. Please check your entries.");
        }

        for (const msg of messages) {
            const li = document.createElement("li");
            li.classList.add("list-group-item", "text-danger");
            li.textContent = msg;
            errorList.appendChild(li);
        }

        const modal = new bootstrap.Modal(document.getElementById('serverErrorModal'));
        modal.show();
    }

    // (kept) — for single-add path or other surfaces that use a text box error container
    function showErrorMessages(data, container) {
        let message = "";

        try {
            if (data && typeof data === "object") {
                // Preferred: ValidationProblem
                if (data.errors && typeof data.errors === "object") {
                    for (const [key, arr] of Object.entries(data.errors)) {
                        if (Array.isArray(arr)) message += arr.join(" ") + " ";
                    }
                } else {
                    // ModelState-as-dictionary or other dictionary
                    for (const [key, arr] of Object.entries(data)) {
                        if (Array.isArray(arr)) message += arr.join(" ") + " ";
                    }
                    // Fallback to title/detail
                    if (!message && (data.title || data.detail)) {
                        message = `${data.title ?? ""} ${data.detail ?? ""}`;
                    }
                }
            }
        } catch { /* ignore */ }

        if (!message.trim()) message = "An error occurred. Please check your entries.";

        container.textContent = message.trim();
        container.style.display = "block";
    }
});