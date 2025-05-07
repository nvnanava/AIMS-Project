CREATE TABLE Hardware (
    AssetTag INT PRIMARY KEY,
    AssetName NVARCHAR(255) NOT NULL,
    AssetType NVARCHAR(255) NOT NULL,
    Status NVARCHAR(255) NOT NULL,
    Manufacturer NVARCHAR(255) NOT NULL,
    Model NVARCHAR(255) NOT NULL,
    SerialNumber NVARCHAR(255) UNIQUE NOT NULL,
    WarrantyExpiration DATE NOT NULL,
    PurchaseDate DATE NOT NULL
);