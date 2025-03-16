CREATE TABLE Hardware (
    AssetTag int PRIMARY KEY IDENTITY(1,1),
    AssetName NVARCHAR(255) NOT NULL,
    AssetType NVARCHAR(255) NOT NULL,
    Status NVARCHAR(255) NOT NULL,
    Manufacturer NVARCHAR(255) NOT NULL,
    Model NVARCHAR(255) NOT NULL,
    SerialNumber NVARCHAR(255) UNIQUE NOT NULL,
    WarrantyExpiration date NOT NULL,
    PurchaseDate date NOT NULL
)