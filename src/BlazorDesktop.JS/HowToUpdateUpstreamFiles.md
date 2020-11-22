@SteveSandersonMS:

## Updating the upstream/aspnetcore/web.js directory

The contents of this directory come from https://github.com/aspnet/AspNetCore repo. 
I didn't want to use a real git submodule because that's such a giant repo,
and I only need a few files from it here. So instead I used the `git read-tree` technique described at https://stackoverflow.com/a/30386041

One-time setup per working copy:

    git remote add -t master --no-tags aspnetcore https://github.com/aspnet/AspNetCore.git

Then, to update the contents of upstream/aspnetcore/web.js to the latest:

    cd <directory containing this .md file>
    git rm -rf upstream/aspnetcore
    git fetch --depth 1 aspnetcore
    git read-tree --prefix=src/BlazorDesktop.JS/upstream/aspnetcore/web.js -u aspnetcore/master:src/Components/Web.JS
    git commit -m "Get Web.JS files from commit a294d64a45f"

When using these commands, replace:

 * `master` with the branch you want to fetch from. For example, release/5.0-preview8
 * `a294d64a45f` with the SHA of the commit you're fetching from

Longer term, we may consider publishing Components.Web.JS as a NuGet package
with embedded .ts sources, so that it's possible to use inside a WebPack build
without needing to clone its sources.

### If you get error: TypeScript produced no output: 
- Synchronize modules in package.josn with ones in ://github.com/dotnet/aspnetcore/blob/release/5.0-rc2/src/Components/Web.JS/package.json 
- run npm i
- Comment imports of MonoPlatforms.ts and all functions that use this module.
- `npm run build:debug` should produce no error. 
- Rebuild BlazorDesktop.JS and other projets that depend on it.

### if you get error: No renderer with id...
Microsoft has change the signature of JSInterop calls in @microsoft/dotnet-js-interop 5.0.0-rc.2.20475.17. 
They added targetInstanceId into `findJSFunction` https://github.com/dotnet/aspnetcore/commit/8a2f29bb539497f79ac3ac1f7d8c35efe88e6dce#diff-f29274668e6528d1f50980a3144898d1243365ad4c788293f72a03a21c94bdbe
So, we need to update typescript type definitions and call with `targetInstanceId == 0` if we want to updat to the latest JSInterop version

