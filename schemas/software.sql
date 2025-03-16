CREATE TABLE Software (
  SoftwareID INT PRIMARY KEY IDENTITY(1,1),
  SoftwareName NVARCHAR(255) NOT NULL,
  SoftwareType NVARCHAR(255) NOT NULL,
  SoftwareVersion NVARCHAR(255) NOT NULL,
  SoftwareDeploymentLocation NVARCHAR(255) NOT NULL,
  SoftwareLicenseKey NVARCHAR(255) UNIQUE NOT NULL,
  SoftwareLicenseExpiration DATE NULL,
  SoftwareUsageData BIGINT NOT NULL,
  SoftwareCost DECIMAL(10,2) NOT NULL
);