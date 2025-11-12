document.addEventListener('DOMContentLoaded', function () {
    // ---- Prevent double-wiring if the script is included twice ----
    if (window.AIMS?.__wiredSoftware) return;
    window.AIMS = window.AIMS || {};
    window.AIMS.__wiredSoftware = true;

    const softwareForm = document.getElementById('SoftwareAddForm'); //phase 1 form
    const addSoftwareModal = document.getElementById('addSoftwareModal');
    const softwareErrorBox = document.getElementById('softwareErrorMessage');

    const licenseForm = document.getElementById('licenseDetailsForm'); //phase 2 form
    const softwareDetailsModal = document.getElementById('softwareDetailsModal');
    const licenseStep = document.getElementById('licenseStep');
    const softwarePreviewList = document.getElementById('softwarePreviewList');
    const nextLicenseBtn = document.getElementById('nextLicenseBtn');
    const submitSoftwareBtn = document.getElementById('submitSoftwareBtn');

    // Phase 1 fields we’ll constrain
    const expInput = document.getElementById('softwareLicenseExpiration');
    const costInput = document.getElementById('softwareCost');

    // Make sure action buttons are not implicit submits
    if (nextLicenseBtn) nextLicenseBtn.type = 'button';
    if (submitSoftwareBtn) submitSoftwareBtn.type = 'button';

    // Block native form submits (Enter key, implicit submit buttons, etc.)
    if (softwareForm) softwareForm.addEventListener('submit', (e) => e.preventDefault());
    if (licenseForm) licenseForm.addEventListener('submit', (e) => e.preventDefault());

    let baseSoftware = {};
    let licenseCount = 0;
    let licenses = [];
    let currentIndex = 0;
    let inTransition = false;
    let editIndex = null;
    let isSubmitting = false;

    // ---------------- utils ----------------
    function todayLocalYMD() {
        // local (not UTC) "YYYY-MM-DD"
        const d = new Date();
        d.setHours(0, 0, 0, 0);
        const tz = d.getTimezoneOffset();
        const local = new Date(d.getTime() - tz * 60 * 1000);
        return local.toISOString().slice(0, 10);
    }

    //helpers
    function setError(id, message) {
        const input = document.getElementById(id);
        if (!input) return;
        input.classList.add("is-invalid");
        input.placeholder = message;
        const errorElem = document.getElementById(id + "Error");
        if (errorElem) errorElem.textContent = message;
    }
    function clearError(id) {
        const input = document.getElementById(id);
        if (!input) return;
        input.classList.remove("is-invalid");
        const errorElem = document.getElementById(id + "Error");
        if (errorElem) errorElem.textContent = "";
    }

    function focusLicenseBox() {
        const box = document.getElementById('licenseKey');
        if (box) box.focus();
    }

    function loadNextLicense() {
        if (currentIndex >= licenseCount) {
            currentIndex = licenseCount;
            return;
        }
        currentIndex++;
        licenseStep.textContent = `License ${licenses.length + 1} of ${licenseCount}`;
        nextLicenseBtn.style.display = "inline-block";
        submitSoftwareBtn.style.display = "none";
        focusLicenseBox();
    }

    async function refreshSoftwareList() {
        if (typeof window.loadAssetsPaged === 'function') {
            try { await window.loadAssetsPaged('Software', 1, 50); return; } catch { /* ignore */ }
        }
        if (typeof window.loadAssets === 'function') {
            try { await window.loadAssets('Software'); return; } catch { /* ignore */ }
        }
        try {
            window.dispatchEvent(new CustomEvent('assets:refresh', { detail: { category: 'Software' } }));
        } catch { /* ignore */ }
        window.location.reload();
    }

    function saveProgress() {
        const data = {
            baseSoftware,
            licenseCount,
            licenses
        };
        localStorage.setItem('saveSoftwareProgress', JSON.stringify(data));
        alert('Software progress saved locally in browser.');
    }

    function loadProgress() {
        const save = localStorage.getItem('saveSoftwareProgress');
        if (!save) {
            alert("No saved progress found.");
            return;
        }
        try {
            const data = JSON.parse(save);
            if (data.baseSoftware) baseSoftware = data.baseSoftware;
            if (data.licenseCount) licenseCount = data.licenseCount;
            if (data.licenses) licenses = data.licenses;

            if (licenses.length >= licenseCount) {
                licenses = licenses.slice(0, licenseCount);
                licenseStep.textContent = `All ${licenseCount} licenses entered. Review and submit.`;
                document.getElementById('licenseInputs').style.display = "none";
                nextLicenseBtn.style.display = "none";
                submitSoftwareBtn.style.display = "inline-block";
            } else {
                currentIndex = licenses.length;
                licenseStep.textContent = `License ${licenses.length + 1} of ${licenseCount}`;
                document.getElementById('licenseInputs').style.display = "block";
                nextLicenseBtn.style.display = "inline-block";
                submitSoftwareBtn.style.display = "none";
                focusLicenseBox();
            }
            renderPreviewList();
        } catch (e) {
            console.error("Error loading saved software progress:", e);
        }
    }

    function clearSaveProgress() {
        localStorage.removeItem("saveSoftwareProgress");
    }

    // ---------------- reset modals ----------------
    addSoftwareModal.addEventListener('hidden.bs.modal', function () {
        if (!inTransition) {
            softwareForm?.reset();
            softwareErrorBox.style.display = "none";
            softwareErrorBox.textContent = "";
            softwareForm?.querySelectorAll('.is-invalid').forEach(el => el.classList.remove('is-invalid'));
            ['softwareName', 'softwareVersion', 'licenseKey', 'softwareLicenseExpiration', 'softwareCost'].forEach(clearError);
        }
        inTransition = false;
    });

    softwareDetailsModal.addEventListener('hidden.bs.modal', function () {
        licenseForm?.reset();
        document.getElementById('licenseInputs').style.display = "block";
        nextLicenseBtn.style.display = "inline-block";
        submitSoftwareBtn.style.display = "none";
        licenseStep.textContent = "";
        softwarePreviewList.innerHTML = "";
        clearError('licenseKey');
    });

    // ---------------- constraints: min date + non-negative cost ----------------
    function applyPhase1Guards() {
        // Expiration cannot be in the past
        if (expInput) {
            const today = todayLocalYMD();
            expInput.setAttribute('min', today);
        }
        // Prevent negative cost
        if (costInput) {
            // Block '-' key
            costInput.addEventListener('keydown', (e) => {
                if (e.key === '-' || e.key === 'Subtract') e.preventDefault();
            });
            // Clamp on input
            const clamp = () => {
                const v = parseFloat(costInput.value);
                if (isNaN(v)) return;
                if (v < 0) costInput.value = Math.abs(v).toString(); // or "0"
            };
            costInput.addEventListener('input', clamp);
            costInput.addEventListener('blur', () => {
                const v = parseFloat(costInput.value);
                if (isNaN(v) || v < 0) costInput.value = '0';
            });
        }
    }

    // open modal check for saved progress
    addSoftwareModal.addEventListener('show.bs.modal', function () {
        // re-apply guards every time modal opens (in case the tab sat overnight)
        applyPhase1Guards();

        const saved = localStorage.getItem("saveSoftwareProgress");
        if (saved) {
            if (!confirm("You have saved progress. Opening this modal will clear it. Continue?")) {
                inTransition = true;
                bootstrap.Modal.getInstance(addSoftwareModal).hide();
                inTransition = false;
                return;
            }
            clearSaveProgress();
        }
        baseSoftware = {};
        licenseCount = 0;
        licenses = [];
        softwarePreviewList.innerHTML = "";
    });

    // ---------------- phase 1 start ----------------
    document.getElementById('startSoftwarePhase2Btn').addEventListener('click', function (e) {
        e.preventDefault();

        let valid = true;
        const requiredFields = [
            { id: "softwareName", message: "Software name is required" },
            { id: "softwareVersion", message: "Version is required" },
            { id: "licenseCount", message: "Must be at least 1" },
            { id: "softwareLicenseExpiration", message: "Expiration date is required" }
        ];

        requiredFields.forEach(field => {
            const input = document.getElementById(field.id);
            if (!input.value.trim()) {
                setError(field.id, field.message);
                valid = false;
            } else {
                clearError(field.id);
            }
        });

        // Extra guard: expiration must be today or future
        if (expInput && expInput.value) {
            const today = todayLocalYMD();
            if (expInput.value < today) {
                setError('softwareLicenseExpiration', 'Expiration cannot be in the past.');
                valid = false;
            } else {
                clearError('softwareLicenseExpiration');
            }
        }

        // Extra guard: cost must be >= 0
        if (costInput) {
            const v = parseFloat(costInput.value);
            if (!isNaN(v) && v < 0) {
                setError('softwareCost', 'Cost cannot be negative.');
                valid = false;
            } else {
                clearError('softwareCost');
            }
        }

        licenseCount = parseInt(document.getElementById('licenseCount').value, 10);
        if (isNaN(licenseCount) || licenseCount <= 0) {
            setError("licenseCount", "Must be at least 1");
            valid = false;
        }

        if (!valid) return;

        baseSoftware = {
            SoftwareName: document.getElementById('softwareName').value.trim(),
            SoftwareVersion: document.getElementById('softwareVersion').value.trim(),
            SoftwareType: "Software",
            SoftwareLicenseExpiration: expInput?.value.trim() || null,
            SoftwareCost: parseFloat(costInput?.value) || 0,
            Status: "Available",
            Comment: "Added in bulk upload"
        };

        licenses = [];
        softwarePreviewList.innerHTML = "";
        inTransition = true;
        bootstrap.Modal.getInstance(addSoftwareModal).hide();
        const sModal = new bootstrap.Modal(softwareDetailsModal, { backdrop: 'static', keyboard: false });
        sModal.show();
        inTransition = false;

        // initialize the first step and focus the input
        currentIndex = 0;
        loadNextLicense();
    });

    // ---------------- phase 2 license entry ----------------
    nextLicenseBtn.addEventListener('click', function (e) {
        e.preventDefault();
        const keyEl = document.getElementById('licenseKey');
        const keyInput = (keyEl?.value || '').trim();

        clearError('licenseKey');

        if (!keyInput) {
            setError('licenseKey', 'License Key is required.');
            focusLicenseBox();
            return;
        }

        if (licenses.some(l => (l.SoftwareLicenseKey || '').trim().toLowerCase() === keyInput.toLowerCase())) {
            setError('licenseKey', 'This License Key has already been entered in this batch.');
            focusLicenseBox();
            return;
        }

        licenses.push({
            ...baseSoftware,
            SoftwareLicenseKey: keyInput,
        });
        renderPreviewList();

        // Reset input field for next entry
        if (keyEl) keyEl.value = "";

        if (licenses.length >= licenseCount) {
            licenseStep.textContent = `All ${licenseCount} licenses entered. Review and submit.`;
            document.getElementById('licenseInputs').style.display = "none";
            nextLicenseBtn.style.display = "none";
            submitSoftwareBtn.style.display = "inline-block";
        } else {
            loadNextLicense();
        }
    });

    // ---------------- submit software licenses ----------------
    submitSoftwareBtn.addEventListener('click', async function (e) {
        e.preventDefault();
        if (isSubmitting) return;
        isSubmitting = true;

        try {
            // If user typed the last key but didn't click "Next", include it
            const keyInput = document.getElementById('licenseKey');
            const pendingKey = (keyInput?.value || '').trim();
            if (pendingKey && licenses.length < licenseCount) {
                if (!licenses.some(l => (l.SoftwareLicenseKey || '').trim().toLowerCase() === pendingKey.toLowerCase())) {
                    licenses.push({ ...baseSoftware, SoftwareLicenseKey: pendingKey });
                    renderPreviewList();
                }
                if (keyInput) keyInput.value = '';
            }

            if (licenses.length === 0) {
                setError('licenseKey', 'License Key is required.');
                focusLicenseBox();
                isSubmitting = false;
                return;
            }

            // Build payload (array at root to match [FromBody] List<CreateSoftwareDto>)
            const cleaned = licenses.map(l => ({
                SoftwareName: (l.SoftwareName || '').trim(),
                SoftwareType: (l.SoftwareType || 'Software').trim(),
                SoftwareVersion: (l.SoftwareVersion || '').trim(),
                SoftwareLicenseKey: (l.SoftwareLicenseKey || '').trim(),
                SoftwareLicenseExpiration: l.SoftwareLicenseExpiration || null,
                SoftwareCost: typeof l.SoftwareCost === 'number' ? l.SoftwareCost : parseFloat(l.SoftwareCost) || 0,
                Status: (l.Status || 'Available').trim(),
                Comment: (l.Comment || '').trim()
            }));

            const data = await aimsFetch('/api/software/add-bulk', {
                method: 'POST',
                ttl: 0, //don't cache
                body: JSON.stringify(cleaned)
            });

            const modal = bootstrap.Modal.getInstance(softwareDetailsModal);
            if (modal) modal.hide();
            await new Promise(r => setTimeout(r, 250));
            clearSaveProgress();
            await refreshSoftwareList();
            return;
        } catch (err) {
            if (err.name === 'AbortError') {
                return;
            }
            if (err.isValidation && err.data) {
                showServerErrorsInline(err.data);
                return;
            }
            // 3) Fallback unexpected error
            showErrorMessages(
                {
                    title: "Server error",
                    detail: err?.data?.message || err?.message || "Unexpected error"
                },
                errorBox
            );
        }

        isSubmitting = false;
    });


    // ---------------- save/load progress ----------------
    const saveBtn = document.getElementById('saveSoftwareProgress');
    if (saveBtn) {
        saveBtn.addEventListener('click', saveProgress);
    }

    const loadBtn = document.getElementById('loadSoftwareProgressBtn');
    if (loadBtn) {
        loadBtn.addEventListener('click', function (e) {
            e.preventDefault();

            const saved = localStorage.getItem("saveSoftwareProgress");
            if (!saved) {
                alert("No saved progress found.");
                return;
            }

            const addSoftwareModalInstance = bootstrap.Modal.getInstance(document.getElementById('addSoftwareModal'));
            if (addSoftwareModalInstance) addSoftwareModalInstance.hide();

            const softwareDetailsModalInstance = new bootstrap.Modal(document.getElementById('softwareDetailsModal'), {
                backdrop: 'static',
                keyboard: false
            });
            softwareDetailsModalInstance.show();
            loadProgress();
        });
    }

    // ---------------- preview list ----------------
    function renderPreviewList() {
        softwarePreviewList.innerHTML = "";
        licenses.forEach((license, index) => {
            const li = document.createElement('li');
            li.className = "list-group-item d-flex justify-content-between align-items-center";
            li.dataset.index = index;
            const span = document.createElement('span');
            span.textContent = `${license.SoftwareLicenseKey}`;
            li.appendChild(span);

            const editBtn = document.createElement('button');
            editBtn.type = "button";
            editBtn.className = "action-btn blue-pencil";
            editBtn.innerHTML = `
    <svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" fill="#032447ff" class="bi bi-pencil" viewBox="0 0 16 16">
        <path d="M12.146.854a.5.5 0 0 1 .708 0l2.292 2.292a.5.5 0 0 1 0 .708l-10 10a.5.5 0 0 1-.168.11l-4 1a.5.5 0 0 1-.62-.62l1-4a.5.5 0 0 1 .11-.168l10-10zM11.207 2.5 13.5 4.793 12.5 5.793 10.207 3.5l1-1zm1.586 1.586-1-1L3 11.293V12h.707L12.793 4.086z"/>
    </svg>
            `;
            editBtn.addEventListener('click', () => startEditLicense(index));
            li.appendChild(editBtn);
            softwarePreviewList.appendChild(li);
        });
    }

    function startEditLicense(index) {
        editIndex = index;
        const item = licenses[index];
        document.getElementById('editLicenseKey').value = item.SoftwareLicenseKey;

        const editModalEl = document.getElementById('editSoftwareModal');
        const editModal = new bootstrap.Modal(editModalEl);
        editModal.show();
    }

    document.getElementById('saveSoftwareEditBtn').addEventListener("click", () => {
        if (editIndex !== null) {
            licenses[editIndex].SoftwareLicenseKey = document.getElementById('editLicenseKey').value.trim();
            renderPreviewList();

            const editModalEl = document.getElementById('editSoftwareModal');
            const modalInstance = bootstrap.Modal.getInstance(editModalEl);
            modalInstance.hide();

            editIndex = null;
        }
    });

    // ---------------- error wiring ----------------
    //Show server side errors inline (handles Dtos[0], Items[2], [1], or "0")
    function showServerErrorsInline(data) {
        softwarePreviewList.querySelectorAll("li").forEach(li => {
            li.classList.remove("list-group-item-danger");
            const existingError = li.querySelector(".inline-error");
            if (existingError) existingError.remove();
        });

        const errors = data?.errors || data;
        if (!errors) {
            if (data?.error) showServerErrors({ error: data.error });
            return;
        }

        let anyShown = false;

        for (const key in errors) {
            const messages = Array.isArray(errors[key]) ? errors[key] : [errors[key]];

            let index = NaN;
            const bracketMatch = key.match(/\[(\d+)\]/);
            if (bracketMatch) {
                index = parseInt(bracketMatch[1], 10);
            } else if (/^\d+$/.test(key)) {
                index = parseInt(key, 10);
            }

            if (!isNaN(index) && softwarePreviewList.children[index]) {
                const li = softwarePreviewList.children[index];
                li.classList.add("list-group-item-danger");
                const errorDiv = document.createElement("div");
                errorDiv.className = "inline-error text-danger small";
                errorDiv.textContent = messages.join(", ");
                li.appendChild(errorDiv);
                anyShown = true;
            }
        }

        if (!anyShown) {
            const summary = Object.entries(errors)
                .map(([k, v]) => `${k}: ${Array.isArray(v) ? v.join(", ") : v}`)
                .join("  ");
            showServerErrors({ error: summary || "An error occurred while saving." });
        }
    }

    //show generic server errors
    function showServerErrors(data) {
        softwareErrorBox.textContent = data.error || "An unknown error occurred.";
        softwareErrorBox.style.display = "block";
    }
});