window.assetFormCache = {};

document.addEventListener('DOMContentLoaded', function () {
    const addAssetModal = document.getElementById('addAssetModal');
    const itemDetailsModal = document.getElementById('itemDetailsModal');
    const assetForm = document.getElementById('AssetAddForm');
    const itemForm = document.getElementById('ItemDetailsForm');
    const assetFormError = document.getElementById('assetFormError');
    const errorBox = document.getElementById('serverErrorMessage');
    const categoryStore = document.getElementById('categoryStore');

    let currentItems = [];
    let currentIndex = 0;
    let itemCount = 1;

    // ---------------- Reset Modals ----------------
    addAssetModal.addEventListener('hidden.bs.modal', resetPhase1);
    itemDetailsModal.addEventListener('hidden.bs.modal', resetPhase2);

    function resetPhase1() {
        assetForm.reset();
        assetFormError.style.display = "none";
        errorBox.style.display = "none";
        assetForm.querySelectorAll('.is-invalid').forEach(el => el.classList.remove('is-invalid'));
    }

    function resetPhase2() {
        itemForm.reset();
        document.getElementById('previewList').innerHTML = "";
        currentItems = [];
        currentIndex = 0;
        itemCount = 1;
    }

    // ---------------- Auto-fill Warranty (+1 year) ----------------
    const purchaseInput = document.getElementById('addPurchaseDate');
    const warrantyInput = document.getElementById('warrantyExpiration');
    purchaseInput?.addEventListener('change', function () {
        if (!purchaseInput.value) return;
        const pd = new Date(purchaseInput.value);
        const nextYear = new Date(pd.setFullYear(pd.getFullYear() + 1));
        warrantyInput.value = nextYear.toISOString().split('T')[0];
    });

    // ---------------- Populate & Free-type Support ----------------
    const manufacturerSelect = document.getElementById('manufacturer');
    const modelSelect = document.getElementById('model');

    async function populateDropdown(selectEl, apiUrl, defaultOptions = []) {
        let options = [...defaultOptions];
        try {
            const resp = await fetch(apiUrl, { cache: 'no-store' });
            if (resp.ok) {
                const data = await resp.json();
                options = options.concat(data);
            }
        } catch (err) {
            console.error(`Error fetching ${selectEl.id}:`, err);
        }

        selectEl.innerHTML = "";
        options.forEach(opt => {
            const optionEl = document.createElement('option');
            optionEl.value = opt;
            optionEl.textContent = opt;
            selectEl.appendChild(optionEl);
        });

        const otherOption = document.createElement('option');
        otherOption.value = "Other";
        otherOption.textContent = "Other...";
        selectEl.appendChild(otherOption);
    }

    function enableFreeType(selectEl) {
        selectEl.addEventListener('change', function () {
            if (selectEl.value === "Other") {
                const input = document.createElement('input');
                input.type = "text";
                input.className = "form-control mb-2";
                input.placeholder = "Enter custom value";
                input.id = selectEl.id + "_free";
                selectEl.style.display = "none";
                selectEl.parentElement.insertBefore(input, selectEl.nextSibling);

                input.addEventListener('blur', function () {
                    if (!input.value.trim()) {
                        selectEl.value = "";
                    } else {
                        selectEl.value = input.value.trim();
                    }
                    input.remove();
                    selectEl.style.display = "";
                });
            }
        });
    }

    // Initialize dropdowns
    populateDropdown(manufacturerSelect, '/api/hardware/add');
    populateDropdown(modelSelect, '/api/hardware/add');
    enableFreeType(manufacturerSelect);
    enableFreeType(modelSelect);

    // ---------------- Phase 1 Submit ----------------
    assetForm.addEventListener('submit', function (e) {
        e.preventDefault();
        assetFormError.style.display = "none";
        errorBox.style.display = "none";

        const requiredFields = [
            { id: "assetType", message: "Asset Type is required" },
            { id: "manufacturer", message: "Manufacturer is required" },
            { id: "model", message: "Model is required" },
            { id: "addPurchaseDate", message: "Purchase Date is required" },
            { id: "warrantyExpiration", message: "Warranty Expiration is required" },
            { id: "itemCount", message: "Number of Items is required" }
        ];

        let valid = true;
        requiredFields.forEach(field => {
            const input = document.getElementById(field.id);
            if (!input.value.trim()) {
                input.classList.add('is-invalid');
                valid = false;
            } else {
                input.classList.remove('is-invalid');
            }
        });

        if (!valid) return;

        itemCount = parseInt(document.getElementById('itemCount').value, 10) || 1;

        // Phase 2 setup
        currentItems = [];
        currentIndex = 0;
        document.getElementById('itemStep').textContent = `Item 1 of ${itemCount}`;
        document.getElementById('nextItemBtn').style.display = itemCount > 1 ? "inline-block" : "none";
        document.getElementById('submitAllBtn').style.display = itemCount === 1 ? "inline-block" : "none";
        itemDetailsModal.querySelector('#serialNumber').value = "";
        itemDetailsModal.querySelector('#tagNumber').value = "";

        bootstrap.Modal.getOrCreateInstance(itemDetailsModal).show();
    });

    // ---------------- Phase 2 Navigation ----------------
    document.getElementById('nextItemBtn').addEventListener('click', function () {
        const serial = itemForm.querySelector('#serialNumber').value.trim();
        const tag = itemForm.querySelector('#tagNumber').value.trim();

        if (!serial || !tag) {
            itemForm.querySelector('#serialNumber').classList.toggle('is-invalid', !serial);
            itemForm.querySelector('#tagNumber').classList.toggle('is-invalid', !tag);
            return;
        }

        currentItems.push({ SerialNumber: serial, AssetTag: tag });
        updatePreviewList();

        currentIndex++;
        if (currentIndex >= itemCount - 1) {
            document.getElementById('nextItemBtn').style.display = "none";
            document.getElementById('submitAllBtn').style.display = "inline-block";
        }

        itemForm.reset();
        document.getElementById('itemStep').textContent = `Item ${currentIndex + 1} of ${itemCount}`;
    });

    document.getElementById('submitAllBtn').addEventListener('click', async function () {
        const serial = itemForm.querySelector('#serialNumber').value.trim();
        const tag = itemForm.querySelector('#tagNumber').value.trim();

        if (!serial || !tag) {
            itemForm.querySelector('#serialNumber').classList.toggle('is-invalid', !serial);
            itemForm.querySelector('#tagNumber').classList.toggle('is-invalid', !tag);
            return;
        }

        currentItems.push({ SerialNumber: serial, AssetTag: tag });
        await submitAllAssets();
    });

    // ---------------- Preview ----------------
    function updatePreviewList() {
        const ul = document.getElementById('previewList');
        ul.innerHTML = "";
        currentItems.forEach((item, i) => {
            const li = document.createElement('li');
            li.className = "list-group-item";
            li.textContent = `${i + 1}. Serial: ${item.SerialNumber}, Tag: ${item.AssetTag}`;
            ul.appendChild(li);
        });
    }

    // ---------------- Submit to Server ----------------
    async function submitAllAssets() {
        const CreateHardwareDto = currentItems.map(item => ({
            AssetName: manufacturerSelect.value + " " + modelSelect.value,
            AssetType: document.getElementById('assetType').value,
            Status: "Available",
            Manufacturer: manufacturerSelect.value,
            Model: modelSelect.value,
            PurchaseDate: purchaseInput.value,
            WarrantyExpiration: warrantyInput.value,
            SerialNumber: item.SerialNumber,
            AssetTag: item.AssetTag
        }));

        try {
            const res = await fetch("/api/hardware/add-bulk", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(CreateHardwareDto)
            });

            if (!res.ok) {
                const data = await res.json();
                showErrorMessages(data, errorBox);
                return;
            }

            localStorage.setItem("lastAddedAsset", JSON.stringify(CreateHardwareDto));
            const assignToast = new bootstrap.Toast(document.getElementById("assignToast"), { delay: 3000 });
            assignToast.show();
            bootstrap.Modal.getOrCreateInstance(itemDetailsModal).hide();
            bootstrap.Modal.getOrCreateInstance(addAssetModal).hide();

            const urlParams = new URLSearchParams(window.location.search);
            const category = urlParams.get("category") || categoryStore.value;
            await new Promise(resolve => setTimeout(resolve, 250));
            if (typeof loadCategoryPaged === "function") await loadCategoryPaged(category, 1);
        } catch (err) {
            showErrorMessages({ error: "Server error: " + err.message }, errorBox);
        }
    }

    // ---------------- Error Handler ----------------
    function showErrorMessages(data, container) {
        let message = "";
        if (data?.errors) {
            for (const key in data.errors) message += data.errors[key].join(" ") + " ";
        } else if (data?.error) message = data.error;
        else message = "An unknown error occurred.";
        container.innerText = message.trim();
        container.style.display = "block";
    }
});
