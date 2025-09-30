
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
    let currentIndex = 0;
    let items = [];
    let inTransition = false;

    //helpers

    //load next item helper func
    function loadNextItem() {
        itemForm.reset();
        currentIndex++;
        itemStep.textContent = `Item ${items.length + 1} of ${itemCount}`;
        nextItemBtn.style.display = "inline-block";
        submitAllBtn.style.display = "none";
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
            itemCount,          // use variable, not DOM lookup
            currentIndex: items.length, // or drop it entirely
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
            if (data.items) items = data.items;

            currentIndex = items.length;
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
                itemStep.textContent = `All ${itemCount} items entered. Review and submit`;
                document.getElementById('itemInputs').style.display = "none"; // hide the input boxes
                nextItemBtn.style.display = "none";
                submitAllBtn.style.display = "inline-block";
            } else {
                itemStep.textContent = `Item ${items.length + 1} of ${itemCount}`;
                document.getElementById('itemInputs').style.display = "block";
                nextItemBtn.style.display = "inline-block";
                submitAllBtn.style.display = "none";
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
    //begin phase 1 - collect base asset data and number of items 
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
            const errorElem = document.getElementById(field.errorId);
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
            PurchaseDate: document.getElementById('addPurchaseDate').value.trim(),
            WarrantyExpiration: document.getElementById('warrantyExpiration').value.trim(),
            Status: "Available"
        }


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

    //Phase 2 - enter in tags/serial#
    nextItemBtn.addEventListener('click', function (e) {
        e.preventDefault();

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

        if (!valid) return;
        //check for duplicates in the current batch
        //Serial Number duplicate check
        if (items.some(i => i.SerialNumber === serial)) {
            setError('serialNumber', 'Duplicate serial number in this batch.');
            return;
        }
        //Tag Number duplicate check
        if (items.some(i => i.AssetTag === tag)) {
            setError('tagNumber', 'Duplicate tag number in this batch.');
            return;
        }
        //clear any previous error states
        // If valid, clear any lingering error messages
        clearError('serialNumber');
        clearError('tagNumber');

        //after validation, add to items array
        items.push({
            ...baseData,
            AssetName: `${baseData.Manufacturer} ${baseData.Model}`.trim(), //concat make/model for name
            SerialNumber: serial.trim(),
            AssetTag: tag.trim()
        });

        //update preview list
        const li = document.createElement("li");
        li.classList.add("list-group-item");
        li.textContent = `${serial} | ${tag}`;
        previewList.appendChild(li);

        //if we just enter the last item, hide next button and show submit button
        if (currentIndex === itemCount) {
            itemStep.textContent = `All ${itemCount} items entered. Review and submit`;
            document.getElementById('itemInputs').style.display = "none"; // hide the input boxes
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

    //Submit all items to server
    submitAllBtn.addEventListener('click', async function (e) {
        e.preventDefault();

        const serial = document.getElementById('serialNumber').value.trim();
        const tag = document.getElementById('tagNumber').value.trim();
        if (serial && tag && items.length < itemCount) {
            items.push({
                ...baseData,
                SerialNumber: serial,
                AssetTag: tag,
                AssetName: `${baseData.Manufacturer} ${baseData.Model}`.trim(), //concat make/model for name
            });
        }

        try {
            const res = await fetch("/api/hardware/add-bulk", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "Accept": "application/json"
                },
                body: JSON.stringify(items)
            });
            if (res.ok) {
                inTransition = false;
                bootstrap.Modal.getInstance(itemDetailsModal).hide();
                await new Promise(resolve => setTimeout(resolve, 250)); // delay for 250ms. 
                await loadAssetsPaged(baseData.AssetType, 1, 50);
                return;
            }
            // server side validation errors
            const data = await res.json();
            showServerErrors(data);
        } catch (err) {
            showServerErrors({ error: "Server response error: " + err.message });
            inTransition = false;
        }
    });

    //helper to show error messages in the server error modal
    function showServerErrors(data) {
        const errorList = document.getElementById("serverErrorList");
        errorList.innerHTML = "";

        let messages = [];

        if (data?.errors) {
            for (const key in data.errors) {
                messages.push(...data.errors[key]);
            }
        } else if (typeof data === "object") { //for mismatched/unexpected data errors. Need to refine or keep.
            for (const key in data) {
                if (Array.isArray(data[key])) {
                    messages.push(...data[key]);
                }
            }
        } else if (data?.error) {
            messages.push(data.error);
        } else {
            messages.push("An unknown error occurred.");
        }
        messages.forEach(msg => {
            const li = document.createElement("li");
            li.classList.add("list-group-item", "text-danger");
            li.textContent = msg;
            errorList.appendChild(li);
        });

        const modal = new bootstrap.Modal(document.getElementById('serverErrorModal'));
        modal.show();
    }

    function showErrorMessages(data, container) {
        let message = "";
        if (data?.errors) {
            for (const key in data.errors) {
                if (data.errors.hasOwnProperty(key)) {
                    message += data.errors[key].join(" ") + " ";
                }
            }
        } else if (typeof data === "object") { //for mismatched/unexpected data errors. Need to refine or keep.
            for (const key in data) {
                if (Array.isArray(data[key])) {
                    message += data[key].join(" ") + " ";
                }
            }
        } else if (data?.error) {
            message = data.error;
        } else {
            message = "An unknown error occurred.";
        }
        container.innerText = message.trim();
        container.style.display = "block";
    }
});
