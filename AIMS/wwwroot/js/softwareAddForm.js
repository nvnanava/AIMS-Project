document.addEventListener('DOMContentLoaded', function () {
    const SoftwareAddForm = document.getElementById('SoftwareAddForm');
    const addSoftwareModal = document.getElementById('addSoftwareModal');
    const softwareErrorMessage = document.getElementById('softwareErrorMessage');

    //reset modal on close
    addSoftwareModal.addEventListener('hidden.bs.modal', function () {
        SoftwareAddForm.reset();
        softwareErrorMessage.style.display = 'none';
        softwareErrorMessage.innerText = '';

        // Clear validation states
        SoftwareAddForm.querySelectorAll('.is-invalid').forEach(el => el.classList.remove('is-invalid'));
    });



    document.getElementById('SoftwareAddForm').addEventListener('submit', async function (e) {
        e.preventDefault(); // Prevent form submission

        // Clear previous error message
        const errorMessageDiv = document.getElementById('softwareErrorMessage');
        errorMessageDiv.style.display = 'none';
        errorMessageDiv.innerText = '';

        //required fields are: Name, Version, License Key, Cost
        // we can also move these error messages directly inline with the classes 
        //to avoid hardcoding them here. May not be needed but might want to clean up if we have time.
        const requiredFields = [
            { id: 'softwareName', message: 'software name required.' },
            { id: 'softwareVersion', message: 'Version number required.' }, //version is mandatory now, but we might want to ask client if they need this specified to be able to add.
            { id: 'softwareLicenseKey', message: 'License Key required.' },
            { id: 'softwareCost', message: 'software cost required.' }
        ];

        // Validate required fields
        //will show inline red error warnings with the above messages if corresponding field is not entered.
        let valid = true;
        requiredFields.forEach(field => {
            const inputElem = document.getElementById(field.id);
            if (inputElem) {
                if (!inputElem.value.trim()) {
                    inputElem.classList.add("is-invalid");
                    inputElem.value = "";
                    inputElem.placeholder = field.message;
                    valid = false;
                } else {
                    inputElem.classList.remove("is-invalid");
                    inputElem.placeholder = "";
                }
            }
        });

        if (!valid) return;

        // Gather form data
        const CreateSoftwareDto = {
            SoftwareName: document.getElementById('softwareName').value.trim(),
            SoftwareType: "Software",
            SoftwareVersion: document.getElementById('softwareVersion').value.trim(),
            SoftwareLicenseKey: document.getElementById('softwareLicenseKey').value.trim(),
            SoftwareLicenseExpiration: document.getElementById('softwareLicenseExpiration').value || null,
            SoftwareCost: parseFloat(document.getElementById('softwareCost').value) || 0
        };


        // Send data to server via AJAX
        try {
            const res = await fetch('/api/software/add', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(CreateSoftwareDto)
            })
            if (!res.ok) {
                const err = await res.json();
                throw new Error(`Failed to update asset: ${Object.values(err).flat().join(' ')}`); 
            }
            const modalElem = document.getElementById('addSoftwareModal');
            const modal = bootstrap.Modal.getInstance(modalElem);
            if (modal) {
                modal.hide();
            }
            //update softwware list
            //and wait for 250ms to ensure backend has processed the new software
            await new Promise(resolve => setTimeout(resolve, 250));
            loadAssets("Software");

        } catch (error) {
                errorMessageDiv.innerText = error.message;
                errorMessageDiv.style.display = 'block';
        }
    });
});