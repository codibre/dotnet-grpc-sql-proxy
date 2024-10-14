# Installation
> `npm install --save @types/lint-staged`

# Summary
This package contains type definitions for lint-staged (https://github.com/okonet/lint-staged#readme).

# Details
Files were exported from https://github.com/DefinitelyTyped/DefinitelyTyped/tree/master/types/lint-staged.
## [index.d.ts](https://github.com/DefinitelyTyped/DefinitelyTyped/tree/master/types/lint-staged/index.d.ts)
````ts
// This exists so lint-staged.config.js can do this:
// /**
//  * @type {import('lint-staged').Config}
// */
// export default { ... };

export type Command = string;

export type Commands = Command[] | Command;

export type ConfigFn = (filenames: string[]) => Commands | Promise<Commands>;

export type Config = ConfigFn | { [key: string]: Command | ConfigFn | Array<Command | ConfigFn> };

````

### Additional Details
 * Last updated: Sat, 16 Dec 2023 11:35:38 GMT
 * Dependencies: none

# Credits
These definitions were written by [Andrey Okonetchnikov](https://github.com/okonet), and [Saiichi Hashimoto](https://github.com/saiichihashimoto).
