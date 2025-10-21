document.addEventListener('DOMContentLoaded', function () {
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

    let baseSoftware = {};
    let licenseCount = 0;
    let licenses = [];
    let currentIndex = 0;
    let inTransition = false;
    let editIndex = null;

    //helpers
    function setError(id, message) {
        const input = document.getElementById(id);
        input.classList.add("is-invalid");
        input.placeholder = message;
        const errorElem = document.getElementById(id + "Error");
        if (errorElem) errorElem.textContent = message;
    }
    function clearError(id) {
        const input = document.getElementById(id);
        input.classList.remove("is-invalid");
        const errorElem = document.getElementById(id + "Error");
        if (errorElem) errorElem.textContent = "";
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
    }

    async function refreshSoftwareList() {
        await AIMS.refreshList('Software', 1, 50);
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
            }
            renderPreviewList();
        } catch (e) {
            console.error("Error loading saved software progress:", e);
        }
    }

    function clearSaveProgress() {
        localStorage.removeItem("saveSoftwareProgress");
    }

    // reset modals
    addSoftwareModal.addEventListener('hidden.bs.modal', function () {
        if (!inTransition) {
            softwareForm.reset();
            softwareErrorBox.style.display = "none";
            softwareErrorBox.textContent = "";
            softwareForm.querySelectorAll('.is-invalid').forEach(el => el.classList.remove('is-invalid'));
            ['softwareName', 'softwareVersion'].forEach(clearError);
        }
        inTransition = false;
    });

    softwareDetailsModal.addEventListener('hidden.bs.modal', function () {
        licenseForm.reset();
        document.getElementById('licenseInputs').style.display = "block";
        nextLicenseBtn.style.display = "inline-block";
        submitSoftwareBtn.style.display = "none";
        licenseStep.textContent = "";
        softwarePreviewList.innerHTML = "";
    });

    // open modal check for saved progress
    addSoftwareModal.addEventListener('show.bs.modal', function () {
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

    // phase 1 start
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
            SoftwareLicenseExpiration: document.getElementById('softwareLicenseExpiration').value.trim(),
            SoftwareCost: parseFloat(document.getElementById('softwareCost').value) || 0,
            Status: "Available",
            Comment: "Added in bulk upload"
        };

        licenses = [];
        softwarePreviewList.innerHTML = "";
        inTransition = true;
        bootstrap.Modal.getInstance(addSoftwareModal).hide();
        const licenseDetailsModal = new bootstrap.Modal(softwareDetailsModal, { backdrop: 'static', keyboard: false });
        licenseDetailsModal.show();
        inTransition = false;
        loadNextLicense();
    });

    // phase 2 license entry
    nextLicenseBtn.addEventListener('click', function (e) {
        e.preventDefault();
        const keyInput = document.getElementById('licenseKey').value.trim();

        clearError('licenseKey');

        if (!keyInput) {
            setError('licenseKey', 'License Key is required.');
            return;
        }

        if (licenses.some(l => l.SoftwareLicenseKey === keyInput)) {
            setError('licenseKey', 'This License Key has already been entered in this batch.');
            return;
        }

        licenses.push({
            ...baseSoftware,
            SoftwareLicenseKey: keyInput,
        });
        renderPreviewList();

        document.getElementById('licenseKey').value = "";

        if (licenses.length >= licenseCount) {
            licenseStep.textContent = `All ${licenseCount} licenses entered. Review and submit.`;
            document.getElementById('licenseInputs').style.display = "none";
            nextLicenseBtn.style.display = "none";
            submitSoftwareBtn.style.display = "inline-block";
        } else {
            loadNextLicense();
        }
    });

    // Submit software licenses
    submitSoftwareBtn.addEventListener('click', async function (e) {
        e.preventDefault();

        const keyInput = document.getElementById('licenseKey');
        const pendingKey = (keyInput?.value || '').trim();
        if (pendingKey && licenses.length < licenseCount) {
            if (!licenses.some(l => (l.SoftwareLicenseKey || '').trim() === pendingKey)) {
                licenses.push({ ...baseSoftware, SoftwareLicenseKey: pendingKey });
                renderPreviewList();
            }
            if (keyInput) keyInput.value = '';
        }

        try {
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

            const res = await fetch('/api/software/add-bulk', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
                body: JSON.stringify({ dtos: cleaned })
            });

            if (res.ok) {
                const modal = bootstrap.Modal.getInstance(softwareDetailsModal);
                if (modal) modal.hide();

                await new Promise(r => setTimeout(r, 250));
                clearSaveProgress();
                await refreshSoftwareList();
                return;
            }

            let data;
            try { data = await res.json(); }
            catch { data = { title: `HTTP ${res.status}`, detail: await res.text() }; }

            showServerErrorsInline(data);
        } catch (error) {
            showServerErrors({ error: 'Server response error: ' + (error?.message || error || 'Unknown error') });
        }
    });

    //save progress button
    document.getElementById('saveSoftwareProgress')?.addEventListener('click', saveProgress);

    //load licenses progress
    document.getElementById('loadSoftwareProgressBtn')?.addEventListener('click', function (e) {
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

    // render and manage preview list
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

    //Show server side errors inline
    function showServerErrorsInline(data) {
        softwarePreviewList.querySelectorAll("li").forEach(li => {
            li.classList.remove("list-group-item-danger");
            const existingError = li.querySelector(".inline-error");
            if (existingError) existingError.remove();
        });

        const errors = data.errors || data;
        if (errors) {
            for (const key in errors) {
                const messages = errors[key];
                const index = parseInt(key, 10);
                if (!isNaN(index) && softwarePreviewList.children[index]) {
                    const li = softwarePreviewList.children[index];
                    li.classList.add("list-group-item-danger");
                    const errorDiv = document.createElement("div");
                    errorDiv.className = "inline-error text-danger small";
                    errorDiv.textContent = Array.isArray(messages) ? messages.join(", ") : messages;
                    li.appendChild(errorDiv);
                }
            }
        } else if (data?.error) {
            showServerErrors({ error: data.error });
        }
    }

    //show generic server errors
    function showServerErrors(data) {
        softwareErrorBox.textContent = data.error || "An unknown error occurred.";
        softwareErrorBox.style.display = "block";
    }
});