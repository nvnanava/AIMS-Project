// Function to handle the form submission and shows werror message if any fields are missing.a 
document.addEventListener('DOMContentLoaded', function () {
    const categoryStore = document.getElementById('categoryStore');
    const assetForm = document.getElementById('AssetAddForm');
    const addAssetModal = document.getElementById('addAssetModal');
    const assetFormError = document.getElementById('assetFormError');
    const errorBox = document.getElementById('serverErrorMessage');

    //reset the modal when closed    
    addAssetModal.addEventListener('hidden.bs.modal', function () {
        assetForm.reset();
        assetFormError.style.display = "none";
        assetFormError.textContent = "";
        errorBox.style.display = "none";
        errorBox.textContent = "";

        assetForm.querySelectorAll('.is-invalid').forEach(el => el.classList.remove('is-invalid'));
    });

    document.getElementById('AssetAddForm').addEventListener('submit', function (e) {
        e.preventDefault(); // Stay on page
        const assetFormError = document.getElementById('assetFormError');
        assetFormError.style.display = "none";
        assetFormError.textContent = "";


        // Validate required fields
        const requiredFields = [
            { id: "assetType", message: "Asset Type is required" },
            { id: "manufacturer", message: "Manufacturer is required", errorId: "manufacturerError" },
            { id: "model", message: "Model is required", errorId: "modelError" },
            { id: "serialNumber", message: "Serial Number is required", errorId: "serialNumberError" },
            { id: "tagNumber", message: "Tag Number is required", errorId: "tagNumberError" },
            { id: "addPurchaseDate", message: "Purchase Date is required", errorId: "purchaseDateError" },
            { id: "warrantyExpiration", message: "Warranty Expiration is required", errorId: "warrantyExpirationError" }
        ];

        let valid = true;

        //loop through required fields and show error if any are missing.
        requiredFields.forEach(field => {
            const input = document.getElementById(field.id);
            const errorElem = document.getElementById(field.errorId);
            if (!input.value.trim()) {
                input.classList.add("is-invalid");
                input.value = "";
                input.placeholder = field.message;
                valid = false;
            } else {
                input.classList.remove("is-invalid");
                input.placeholder = "";
            }
        });

        if (!valid) return;

        //build the dto object to send to the server.
        const CreateHardwareDto = {
            AssetTag: document.getElementById('tagNumber').value.trim(),
            AssetName: document.getElementById('manufacturer').value + " " + document.getElementById('model').value,
            AssetType: document.getElementById('assetType').value,
            Status: "Available", // Default status
            Manufacturer: document.getElementById('manufacturer').value,
            Model: document.getElementById('model').value,
            SerialNumber: document.getElementById('serialNumber').value.trim(),
            WarrantyExpiration: document.getElementById('warrantyExpiration').value,
            PurchaseDate: document.getElementById('addPurchaseDate').value
        };

        //send data if all required fields are filled.

        fetch("/api/hardware/add", {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "Accept": "application/json"
            },
            body: JSON.stringify(CreateHardwareDto)
        }).then(async res => {
            const errorBox = document.getElementById('serverErrorMessage');
            try {
                if (res.ok) {
                    errorBox.style.display = 'none';
                    const assignToast = new bootstrap.Toast(document.getElementById("assignToast"), { delay: 3000 });
                    assignToast.show();

                    $('#addAssetModal').modal('hide');

                    const urlParams = new URLSearchParams(window.location.search);
                    const category = urlParams.get("category") || categoryStore.value

                    await new Promise(resolve => setTimeout(resolve, 250)); // delay for 250ms. 
                    await loadAssets(category); //optimistically reload assets. Hardware controller now bumps cache stamp.
                    return;
                }
                const data = await res.json();
                showErrorMessages(data, errorBox);
            } catch (err) {
                showErrorMessages({ error: "Server response error: " + err.message }, errorBox);
            }
        });
    });
});


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