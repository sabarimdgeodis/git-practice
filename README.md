# Trailer Condition Function

The isolated Azure Function in `Functions` creates a `GeoTruckRequests` SharePoint
list item for each form submission, creates a folder named with that item's
SharePoint `UniqueId` in the `Shared Documents` library, uploads submitted
photos, and writes their URLs to the item's photo hyperlink fields.

The Function uses `DefaultAzureCredential`. Locally, sign in with a developer
credential that can access the SharePoint site. In Azure, enable the Function
App's managed identity and grant it the required SharePoint permissions and
site-level write access. The identity needs permission to create and update
list items, create folders, and upload files.

Configure these Function App settings (the first three are also in the local
`Functions/local.settings.json`):

- `SharePointSiteUrl`: `https://geodis.sharepoint.com/sites/log-AMS-Power-BI-Recertification`
- `SharePointListTitle`: `GeoTruckRequests`
- `SharePointLibraryTitle`: `Shared Documents`

The SharePoint photo columns must be hyperlink columns with internal names
`Front_x0020_Photo` and `Driver_x0020_Side_x0020_Photo`.