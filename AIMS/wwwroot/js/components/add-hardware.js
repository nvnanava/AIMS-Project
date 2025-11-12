document.addEventListener('DOMContentLoaded', function () {
    if (window.AIMS?.__wiredHardware) return;
    window.AIMS = window.AIMS || {};
    window.AIMS.__wiredHardware = true;

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
        clearError('serialNumber');
        clearError('tagNumber');
        currentIndex = items.length + 1;
        itemStep.textContent = `Item ${currentIndex} of ${itemCount}`;
        nextItemBtn.style.display = "inline-block";
        submitAllBtn.style.display = "none";
        document.getElementById('itemInputs').style.display = "block";

        // Focus the Serial Number input automatically for rapid entry
        const serialEl = document.getElementById('serialNumber');
        if (serialEl) {
            setTimeout(() => serialEl.focus(), 50);
        }
    }

    //helper to set error state on input
    function setError(id, message) {
        const input = document.getElementById(id);
        if (!input) return;
        input.classList.add("is-invalid");
        const errorElem = document.getElementById(id + "Error");
        if (errorElem) errorElem.textContent = message;
    }
    //helper to clear error state on input
    function clearError(id) {
        const input = document.getElementById(id);
        if (!input) return;
        input.classList.remove("is-invalid");
        const errorElem = document.getElementById(id + "Error");
        if (errorElem) errorElem.textContent = "";
    }

    function setEditError(inputId, errorId, msg) {
        const input = document.getElementById(inputId);
        const err = document.getElementById(errorId);
        if (input) input.classList.add('is-invalid');
        if (err) err.textContent = msg || '';
    }

    function clearEditError(inputId, errorId) {
        const input = document.getElementById(inputId);
        const err = document.getElementById(errorId);
        if (input) input.classList.remove('is-invalid');
        if (err) err.textContent = '';
    }

    // case-insensitive duplicate checks, ignoring the row being edited
    function hasDuplicateSerial(value, ignoreIndex) {
        const v = (value || '').trim().toLowerCase();
        return items.some((it, idx) => idx !== ignoreIndex && (it.SerialNumber || '').toLowerCase() === v);
    }
    function hasDuplicateTag(value, ignoreIndex) {
        const v = (value || '').trim().toLowerCase();
        return items.some((it, idx) => idx !== ignoreIndex && (it.AssetTag || '').toLowerCase() === v);
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
                itemStep.textContent = `All ${itemCount} items entered. Review and submit`;
                document.getElementById('itemInputs').style.display = "none";
                nextItemBtn.style.display = "none";
                submitAllBtn.style.display = "inline-block";
            } else {
                loadNextItem();
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
            ['manufacturer', 'model', 'itemCount', 'addPurchaseDate', 'warrantyExpiration'].forEach(clearError);
        }
        inTransition = false;
    });

    //reset modal state of phase 2 form
    itemDetailsModal.addEventListener('hidden.bs.modal', function () {
        itemForm.reset();
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
                inTransition = true;
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

        // Set default dates when opening phase 1 modal
        setDefaultPurchaseAndWarranty();
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
        ];

        let valid = true;

        requiredFields.forEach(field => {
            const input = document.getElementById(field.id);
            if (!input.value.trim()) {
                setError(field.id, field.message);
                valid = false;
            } else {
                clearError(field.id);
            }
        });

        const purchaseDateInput = document.getElementById('addPurchaseDate');
        const purchaseDate = new Date(purchaseDateInput.value);
        const today = new Date();
        today.setHours(0, 0, 0, 0);

        if (!purchaseDateInput.value.trim()) {
            setError("addPurchaseDate", "Purchase Date is required");
            valid = false;
        } else if (purchaseDate > today) {
            setError("addPurchaseDate", "Purchase Date cannot be in the future");
            valid = false;
        } else {
            clearError("addPurchaseDate");
        }

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
        inTransition = true;
        bootstrap.Modal.getInstance(addAssetModal).hide();
        new bootstrap.Modal(itemDetailsModal).show();
        inTransition = false;
        loadNextItem();
    });

    // ---------------- Phase 2 - per-item (tags/serials) ----------------

    function addCurrentInputsAsItem() {
        const serial = document.getElementById('serialNumber').value.trim();
        const tag = document.getElementById('tagNumber').value.trim();

        clearError('serialNumber');
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

        if (items.some(i => i.SerialNumber === serial)) {
            setError('serialNumber', 'Duplicate serial number in this batch.');
            return false;
        }
        if (items.some(i => i.AssetTag === tag)) {
            setError('tagNumber', 'Duplicate tag number in this batch.');
            return false;
        }

        items.push({
            ...baseData,
            AssetName: `${baseData.Manufacturer} ${baseData.Model}`.trim(),
            SerialNumber: serial,
            AssetTag: tag
        });

        renderPreviewList();

        return true;
    }

    nextItemBtn.addEventListener('click', function (e) {
        e.preventDefault();

        if (items.length >= itemCount) {
            itemStep.textContent = `All ${itemCount} items entered. Review and submit`;
            document.getElementById('itemInputs').style.display = "none";
            nextItemBtn.style.display = "none";
            submitAllBtn.style.display = "inline-block";
            return;
        }

        if (!addCurrentInputsAsItem()) return;

        if (items.length >= itemCount) {
            itemStep.textContent = `All ${itemCount} items entered. Review and submit`;
            document.getElementById('itemInputs').style.display = "none";
            nextItemBtn.style.display = "none";
            submitAllBtn.style.display = "inline-block";
        } else {
            loadNextItem();
        }
    });

    document.getElementById('saveProgress').addEventListener('click', saveProgress);

    document.getElementById('loadProgressBtn')?.addEventListener('click', function (e) {
        e.preventDefault();

        const saved = localStorage.getItem("assetProgress");
        if (!saved) {
            alert("No saved progress found.");
            return;
        }

        const addAssetModal = bootstrap.Modal.getInstance(document.getElementById('addAssetModal'));
        if (addAssetModal) addAssetModal.hide();

        const itemModal = new bootstrap.Modal(document.getElementById('itemDetailsModal'));
        itemModal.show();
        loadProgress();
    });

    // ---------------- Submit all items to server ----------------
    submitAllBtn.addEventListener('click', async function (e) {
        e.preventDefault();

        if (items.length < itemCount) {
            const serial = document.getElementById('serialNumber').value.trim();
            const tag = document.getElementById('tagNumber').value.trim();
            if (serial && tag) {
                const added = addCurrentInputsAsItem();
                if (!added) return;
            }
        }

        if (items.length < itemCount) {
            setError('serialNumber', 'Please add all items before submitting.');
            return;
        }

        const cleaned = items.map(r => ({
            AssetTag: (r.AssetTag ?? "").trim(),
            Manufacturer: (r.Manufacturer ?? "").trim(),
            Model: (r.Model ?? "").trim(),
            SerialNumber: (r.SerialNumber ?? "").trim(),
            AssetType: (r.AssetType ?? "").trim(),
            Status: (r.Status ?? "").trim(),
            PurchaseDate: r.PurchaseDate,            // "yyyy-MM-dd"
            WarrantyExpiration: r.WarrantyExpiration // "yyyy-MM-dd"
        }));

        // ---- Preflight duplicate check vs DB (soft-fail) ----
        try {
            const pre = await aimsFetch("/api/hardware/check-duplicates", {
                method: "POST",
                ttl: 0, //don't cache
                body: JSON.stringify({ dtos: cleaned })
            });

            const preData = pre;
            const { existingSerials = [], existingTags = [] } = preData;

            const errs = {};
            items.forEach((it, i) => {
                const rowErrors = [];
                const s = (it.SerialNumber || "").trim();
                const t = (it.AssetTag || "").trim();

                if (existingSerials.some(es => (es || "").toLowerCase() === s.toLowerCase())) {
                    rowErrors.push(`Duplicate serial number: ${s}`);
                }
                if (existingTags.includes(t)) {
                    rowErrors.push(`Duplicate asset tag: ${t}`);
                }

                if (rowErrors.length) errs[`Dtos[${i}]`] = rowErrors;
            });

            if (Object.keys(errs).length) {
                showServerErrorsInline({ errors: errs });
                return;
            }

        } catch { /* ignore preflight failure */ }

        try {
            const data = await aimsFetch("/api/hardware/add-bulk", {
                method: "POST",
                ttl: 0, //don't cache
                body: JSON.stringify({ dtos: cleaned })
            });


            clearSaveProgress();

            const modal = bootstrap.Modal.getInstance(itemDetailsModal);
            if (modal) modal.hide();

            await new Promise(r => setTimeout(r, 150));

            let refreshed = false;
            try {
                if (window.AIMS?.refreshList) {
                    await window.AIMS.refreshList(baseData.AssetType, 1, 50, { invalidate: true, scrollTop: true });
                    refreshed = true;
                }
            } catch { /* ignore */ }

            if (!refreshed && typeof window.loadAssetsPaged === 'function') {
                try {
                    await window.loadAssetsPaged(baseData.AssetType, 1, 50, true);
                    refreshed = true;
                } catch { /* ignore */ }
            }

            if (!refreshed) window.location.reload();
            return;
        } catch (err) {
            if (err.name !== "AbortError") {
                // Server-side validation is usually a JSON object
                try {
                    const errorObj = JSON.parse(err.message.replace(/^HTTP \d+:/, "").trim());
                    if (errorObj && typeof errorObj === 'object') {
                        showServerErrorsInline(errorObj);
                        return;
                    }
                } catch { /* non-JSON fallthrough */ }
            }

            // Fallback: unknown or server error → toast/banner
            showErrorMessages(
                { title: "Server error", detail: "Could not save right now. Please try again." },
                errorBox
            );
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
        const item = items[index];
        document.getElementById('editSerialNumber').value = item.SerialNumber;
        document.getElementById('editTagNumber').value = item.AssetTag;

        const editModal = new bootstrap.Modal(document.getElementById('editItemModal'));
        clearEditError('editSerialNumber', 'editSerialError');
        clearEditError('editTagNumber', 'editTagError');
        editModal.show();
    }

    document.getElementById('saveEditBtn').addEventListener("click", function () {
        if (editIndex === null) return;

        const serialInput = document.getElementById('editSerialNumber');
        const tagInput = document.getElementById('editTagNumber');

        const newSerial = (serialInput.value || '').trim();
        const newTag = (tagInput.value || '').trim();

        clearEditError('editSerialNumber', 'editSerialError');
        clearEditError('editTagNumber', 'editTagError');

        let ok = true;

        if (!newSerial) {
            setEditError('editSerialNumber', 'editSerialError', 'Serial Number is required.');
            ok = false;
        } else if (hasDuplicateSerial(newSerial, editIndex)) {
            setEditError('editSerialNumber', 'editSerialError', 'Duplicate serial number in this batch.');
            ok = false;
        }

        if (!newTag) {
            setEditError('editTagNumber', 'editTagError', 'Tag Number is required.');
            ok = false;
        } else if (hasDuplicateTag(newTag, editIndex)) {
            setEditError('editTagNumber', 'editTagError', 'Duplicate tag number in this batch.');
            ok = false;
        }

        if (!ok) return;

        items[editIndex].SerialNumber = newSerial;
        items[editIndex].AssetTag = newTag;
        renderPreviewList();

        const editModalEl = document.getElementById('editItemModal');
        const modalInstance = bootstrap.Modal.getInstance(editModalEl) || new bootstrap.Modal(editModalEl);
        modalInstance.hide();

        editIndex = null;
    });

    function showServerErrorsInline(data) {
        const list = document.getElementById("previewList");
        if (!list) return;

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

    function showErrorMessages(data, container) {
        let message = "";

        try {
            if (data && typeof data === "object") {
                if (data.errors && typeof data.errors === "object") {
                    for (const [key, arr] of Object.entries(data.errors)) {
                        if (Array.isArray(arr)) message += arr.join(" ") + " ";
                    }
                } else {
                    for (const [key, arr] of Object.entries(data)) {
                        if (Array.isArray(arr)) message += arr.join(" ") + " ";
                    }
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

    // ---- Purchase Date protections + defaults + dynamic warranty floor ----
    (function enforcePurchaseDateGuards() {
        const purchaseEl = document.getElementById('addPurchaseDate');
        const warrantyEl = document.getElementById('warrantyExpiration');
        if (!purchaseEl || !warrantyEl) return;

        // Local YYYY-MM-DD (avoids UTC off-by-one issues)
        const todayLocalYMD = () => {
            const d = new Date();
            d.setHours(0, 0, 0, 0);
            const tzOffsetMin = d.getTimezoneOffset();
            const local = new Date(d.getTime() - tzOffsetMin * 60 * 1000);
            return local.toISOString().slice(0, 10);
        };

        const addYearsYMD = (ymd, years = 1) => {
            if (!ymd) return "";
            const [y, m, d] = ymd.split('-').map(n => parseInt(n, 10));
            const dt = new Date(y, (m - 1), d);
            dt.setFullYear(dt.getFullYear() + years);
            const yyyy = String(dt.getFullYear()).padStart(4, '0');
            const mm = String(dt.getMonth() + 1).padStart(2, '0');
            const dd = String(dt.getDate()).padStart(2, '0');
            return `${yyyy}-${mm}-${dd}`;
        };

        const clampDate = (input, { min, max, onInvalid }) => {
            const v = (input.value || '').trim();
            if (!v) return;
            if (min && v < min) {
                input.value = min;
                onInvalid && onInvalid('Purchase Date adjusted to earliest allowed.');
            }
            if (max && input.value > max) {
                input.value = max;
                onInvalid && onInvalid('Purchase Date cannot be in the future.');
            }
        };

        // Enforce warranty >= purchase; update min and clamp if needed.
        const enforceWarrantyFloor = () => {
            const p = (purchaseEl.value || '').trim();
            if (!p) {
                warrantyEl.removeAttribute('min');
                return;
            }
            warrantyEl.setAttribute('min', p);

            const w = (warrantyEl.value || '').trim();
            if (w && w < p) {
                // Clamp to purchase date and flag briefly
                warrantyEl.value = p;
                const err = document.getElementById('warrantyExpirationError');
                if (err) err.textContent = 'Warranty Expiration Date cannot be before Purchase Date';
                warrantyEl.classList.add('is-invalid');
                // Clear the visual error shortly after clamping
                setTimeout(() => {
                    warrantyEl.classList.remove('is-invalid');
                    const e2 = document.getElementById('warrantyExpirationError');
                    if (e2) e2.textContent = '';
                }, 1600);
            } else {
                warrantyEl.classList.remove('is-invalid');
                const err = document.getElementById('warrantyExpirationError');
                if (err) err.textContent = '';
            }
        };

        // Autofill warranty to +1 year when purchase changes (unless user manually overrode)
        const autoFillWarrantyFromPurchase = () => {
            const p = (purchaseEl.value || '').trim();
            if (!p) return;
            if (!warrantyEl.value || warrantyEl.dataset.autofilled === 'true') {
                warrantyEl.value = addYearsYMD(p, 1);
                warrantyEl.dataset.autofilled = 'true';
            }
        };

        // Exposed so we can call it on modal open
        window.setDefaultPurchaseAndWarranty = function setDefaultPurchaseAndWarranty() {
            const today = todayLocalYMD();
            if (!purchaseEl.value) purchaseEl.value = today;
            enforceWarrantyFloor();
            if (!warrantyEl.value) {
                warrantyEl.value = addYearsYMD(purchaseEl.value || today, 1);
                warrantyEl.dataset.autofilled = 'true';
            }
        };

        // Re-apply today's max and normalize current value
        const refreshMaxAndClamp = () => {
            const today = todayLocalYMD();
            purchaseEl.setAttribute('max', today);
            clampDate(purchaseEl, {
                max: today,
                onInvalid: msg => {
                    setError('addPurchaseDate', msg);
                    setTimeout(() => clearError('addPurchaseDate'), 1500);
                }
            });
            enforceWarrantyFloor();
        };

        // Initial guard & defaults on page load
        refreshMaxAndClamp();
        window.setDefaultPurchaseAndWarranty();

        // Track manual edits to warranty (so we don't override user choice)
        warrantyEl.addEventListener('input', () => {
            warrantyEl.dataset.autofilled = 'false';
            enforceWarrantyFloor();
        });
        warrantyEl.addEventListener('change', enforceWarrantyFloor);

        // Keep max fresh each time the modal opens and reset sensible defaults
        const addAssetModalEl = document.getElementById('addAssetModal');
        if (addAssetModalEl) {
            addAssetModalEl.addEventListener('show.bs.modal', () => {
                refreshMaxAndClamp();
                window.setDefaultPurchaseAndWarranty();
            });
        }

        // On purchase change: refresh guards, autofill warranty (+1 year) if not user-edited, then enforce floor
        purchaseEl.addEventListener('input', () => {
            refreshMaxAndClamp();
        });
        purchaseEl.addEventListener('change', () => {
            refreshMaxAndClamp();
            autoFillWarrantyFromPurchase();
            enforceWarrantyFloor(); // <- dynamic protection updates immediately with new purchase date
        });
        purchaseEl.addEventListener('focus', refreshMaxAndClamp);

        // Prevent mouse wheel from incrementing into the future while focused
        purchaseEl.addEventListener('wheel', (e) => {
            if (document.activeElement === purchaseEl) e.preventDefault();
        }, { passive: false });
    })();
});