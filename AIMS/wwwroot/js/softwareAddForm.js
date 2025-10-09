document.addEventListener('DOMContentLoaded', function () {

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
            SoftwareType: "Software", //unsure what we want to do with this
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

        // Reset input field for next entry
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
    
    /*

    Submit software licenses

    */
    
    submitSoftwareBtn.addEventListener('click', async function (e) {
        e.preventDefault();

        const licenseKey = document.getElementById('licenseKey').value.trim();
        if (licenseKey && licenses.length < licenseCount) {
            licenses.push({
                ...baseSoftware,
                SoftwareLicenseKey: licenseKey
            });
            renderPreviewList();
        }

        try {
            const res = await fetch('/api/software/add-bulk', {

                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(licenses)
            });
            if (res.ok) {
                inTransition = false;
                bootstrap.Modal.getInstance(softwareDetailsModal).hide();
                await new Promise(resolve => setTimeout(resolve, 250));
                await loadAssetsPaged("Software", 1, 50); // reload software asset list
                clearSaveProgress();
                return;
            }
            //handle serve side errors
            const data = await res.json();
            showServerErrorsInline(data);
        } catch (error) {
            showServerErrors({ error: "Server response error: " + err.message });
            inTransition = false;
        }
    });


    //save progress button
    const saveBtn = document.getElementById('saveSoftwareProgress');
    if (saveBtn) {
        saveBtn.addEventListener('click', saveProgress);
    }

    //load licenses progress
    const loadBtn = document.getElementById('loadSoftwareProgressBtn');
    if (loadBtn) {
        loadBtn.addEventListener('click', function (e) {
        e.preventDefault();

        // try to load from localStorage
        const saved = localStorage.getItem("saveSoftwareProgress");
        if (!saved) {
            alert("No saved progress found.");
            return;
        }

        const addSoftwareModalInstance = bootstrap.Modal.getInstance(document.getElementById('addSoftwareModal'));
        if (addSoftwareModalInstance) addSoftwareModalInstance.hide();

        //show phase 2 modal and load saved data
        const softwareDetailsModalInstance = new bootstrap.Modal(document.getElementById('softwareDetailsModal'), {
            backdrop: 'static',
            keyboard: false
        });
        softwareDetailsModalInstance.show();
            loadProgress();
            
        });
    }

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
        //on click of edit button, load item data into form
        const item = licenses[index];
        document.getElementById('editLicenseKey').value = item.SoftwareLicenseKey;
        //show slim modal

        const editModalEl = document.getElementById('editSoftwareModal');
        const editModal = new bootstrap.Modal(editModalEl);
        editModal.show();

    }

    document.getElementById('saveSoftwareEditBtn').addEventListener("click", () => {
        if (editIndex !== null) {
            licenses[editIndex].SoftwareLicenseKey = document.getElementById('editLicenseKey').value.trim();
            renderPreviewList();

            // close modal
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

        // Accept both { errors: {...} } and { 0: [...] }
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









    // const SoftwareAddForm = document.getElementById('SoftwareAddForm');
    // const addSoftwareModal = document.getElementById('addSoftwareModal');
    // const softwareErrorMessage = document.getElementById('softwareErrorMessage');

    // //reset modal on close
    // addSoftwareModal.addEventListener('hidden.bs.modal', function () {
    //     SoftwareAddForm.reset();
    //     softwareErrorMessage.style.display = 'none';
    //     softwareErrorMessage.innerText = '';

    //     // Clear validation states
    //     SoftwareAddForm.querySelectorAll('.is-invalid').forEach(el => el.classList.remove('is-invalid'));
    // });



    // document.getElementById('SoftwareAddForm').addEventListener('submit', async function (e) {
    //     e.preventDefault(); // Prevent form submission

    //     // Clear previous error message
    //     const errorMessageDiv = document.getElementById('softwareErrorMessage');
    //     errorMessageDiv.style.display = 'none';
    //     errorMessageDiv.innerText = '';

    //     //required fields are: Name, Version, License Key, Cost
    //     // we can also move these error messages directly inline with the classes 
    //     //to avoid hardcoding them here. May not be needed but might want to clean up if we have time.
    //     const requiredFields = [
    //         { id: 'softwareName', message: 'software name required.' },
    //         { id: 'softwareVersion', message: 'Version number required.' }, //version is mandatory now, but we might want to ask client if they need this specified to be able to add.
    //         { id: 'softwareLicenseKey', message: 'License Key required.' },
    //         { id: 'softwareCost', message: 'software cost required.' }
    //     ];

    //     // Validate required fields
    //     //will show inline red error warnings with the above messages if corresponding field is not entered.
    //     let valid = true;
    //     requiredFields.forEach(field => {
    //         const inputElem = document.getElementById(field.id);
    //         if (inputElem) {
    //             if (!inputElem.value.trim()) {
    //                 inputElem.classList.add("is-invalid");
    //                 inputElem.value = "";
    //                 inputElem.placeholder = field.message;
    //                 valid = false;
    //             } else {
    //                 inputElem.classList.remove("is-invalid");
    //                 inputElem.placeholder = "";
    //             }
    //         }
    //     });

    //     if (!valid) return;

    //     // Gather form data
    //     const CreateSoftwareDto = {
    //         SoftwareName: document.getElementById('softwareName').value.trim(),
    //         SoftwareType: "Software",
    //         SoftwareVersion: document.getElementById('softwareVersion').value.trim(),
    //         SoftwareLicenseKey: document.getElementById('softwareLicenseKey').value.trim(),
    //         SoftwareLicenseExpiration: document.getElementById('softwareLicenseExpiration').value || null,
    //         SoftwareCost: parseFloat(document.getElementById('softwareCost').value) || 0
    //     };


    //     // Send data to server via AJAX
    //     try {
    //         const res = await fetch('/api/software/add', {
    //             method: 'POST',
    //             headers: {
    //                 'Content-Type': 'application/json'
    //             },
    //             body: JSON.stringify(CreateSoftwareDto)
    //         })
    //         if (!res.ok) {
    //             const err = await res.json();
    //             throw new Error(`Failed to update asset: ${ Object.values(err).flat().join(' ') } `); 
    //         }
    //         const modalElem = document.getElementById('addSoftwareModal');
    //         const modal = bootstrap.Modal.getInstance(modalElem);
    //         if (modal) {
    //             modal.hide();
    //         }
    //         //update softwware list
    //         //and wait for 250ms to ensure backend has processed the new software
    //         await new Promise(resolve => setTimeout(resolve, 250));
    //         loadAssets("Software");

    //     } catch (error) {
    //             errorMessageDiv.innerText = error.message;
    //             errorMessageDiv.style.display = 'block';
    //     }
    // });
