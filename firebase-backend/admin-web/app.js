const auth=firebase.auth();
const provider=new firebase.auth.GoogleAuthProvider();
const q=id=>document.getElementById(id);

async function idToken(force=false){
  if(!auth.currentUser) throw new Error("Sign in first.");
  return auth.currentUser.getIdToken(force);
}

async function api(path,options={}){
  const token=await idToken(false);
  const headers={...(options.headers||{}),Authorization:`Bearer ${token}`};
  if(options.body) headers["Content-Type"]="application/json";
  const r=await fetch(path,{...options,headers});
  let data={}; try{data=await r.json()}catch{}
  if(!r.ok){const e=new Error(data.error||`HTTP ${r.status}`);e.status=r.status;throw e}
  return data;
}

function message(text,error=false){
  const el=q("message"); el.textContent=text; el.classList.remove("hidden");
  el.style.background=error?"#fde8e8":"#e9f7ee"; el.style.color=error?"#8c3030":"#245d38";
}

function fmt(v){if(!v)return "-";const d=new Date(v);return isNaN(d)?"-":d.toLocaleString()}
function esc(v){return String(v??"").replaceAll("&","&amp;").replaceAll("<","&lt;").replaceAll(">","&gt;").replaceAll('"',"&quot;").replaceAll("'","&#039;")}

async function isAdmin(){
  const r=await auth.currentUser.getIdTokenResult(true);
  return r.claims.admin===true;
}

async function loadOverview(){
  const d=await api("/api/admin-overview");
  q("usersCount").textContent=d.users??0;
  q("trialCount").textContent=d.entitlements?.trial??0;
  q("expiredCount").textContent=d.entitlements?.expired??0;
  q("installCount").textContent=d.installations??0;
}

async function loadConfig(){
  const d=await api("/api/admin-config"),c=d.config||{};
  q("latestVersion").value=c.latestVersion||"";
  q("minimumVersion").value=c.minimumVersion||"";
  q("premiumEnabled").checked=c.premiumEnabled===true;
  q("googleDriveEnabled").checked=c.googleDriveEnabled===true;
  q("oneDriveEnabled").checked=c.oneDriveEnabled===true;
  q("maintenanceMessage").value=c.maintenanceMessage||"";
}

async function loadUsers(){
  const d=await api("/api/admin-users"),body=q("usersBody");body.innerHTML="";
  for(const u of d.users||[]){
    const tr=document.createElement("tr");
    tr.innerHTML=`<td>${esc(u.email||"(no email)")}</td><td><span class="state ${esc(u.entitlementState)}">${esc(u.entitlementState)}</span></td><td>${esc(fmt(u.trialEndsAtUtc))}</td><td>${esc(fmt(u.lastSignInAtUtc))}</td><td><button class="light">Details</button></td>`;
    tr.querySelector("button").onclick=()=>showUser(u.uid);
    body.appendChild(tr);
  }
}

async function loadAll(){await Promise.all([loadOverview(),loadConfig(),loadUsers()])}

async function showUser(uid){
  try{
    const d=await api(`/api/admin-user?uid=${encodeURIComponent(uid)}`);
    const installs=(d.installations||[]).map(i=>`<div class="install"><b>${esc(i.appVersion||"Unknown version")}</b> Â· ${esc(i.platform||"Windows")}<br>${esc(i.osVersion||"")}<br>Last seen: ${esc(fmt(i.lastSeenAtUtc))}</div>`).join("");
    q("detail").innerHTML=`<h2>${esc(d.user?.email||"User")}</h2><dl class="detail"><dt>User ID</dt><dd>${esc(d.user?.uid)}</dd><dt>Created</dt><dd>${esc(fmt(d.user?.createdAtUtc))}</dd><dt>Last sign-in</dt><dd>${esc(fmt(d.user?.lastSignInAtUtc))}</dd><dt>Entitlement</dt><dd>${esc(d.entitlement?.entitlementState||"none")}</dd><dt>Trial starts</dt><dd>${esc(fmt(d.entitlement?.trialStartedAtUtc))}</dd><dt>Trial ends</dt><dd>${esc(fmt(d.entitlement?.trialEndsAtUtc))}</dd><dt>Premium</dt><dd>${d.entitlement?.premiumEnabled?"Yes":"No"}</dd></dl><h3>Installations</h3>${installs||"<p>No installations recorded.</p>"}`;
    q("detailDialog").showModal();
  }catch(e){message(e.message,true)}
}

q("signin").onclick=()=>auth.signInWithPopup(provider).catch(e=>message(e.message,true));
q("signout").onclick=()=>auth.signOut();
q("closeDialog").onclick=()=>q("detailDialog").close();

q("bootstrapButton").onclick=async()=>{
  try{
    const code=q("bootstrapCode").value.trim();
    if(code.length<12)throw new Error("Enter the bootstrap code.");
    await api("/api/bootstrap-admin",{method:"POST",body:JSON.stringify({bootstrapCode:code})});
    await auth.currentUser.getIdToken(true);
    message("Admin access granted. Reloading...");
    setTimeout(()=>location.reload(),700);
  }catch(e){message(e.message,true)}
};

q("refresh").onclick=()=>loadAll().then(()=>message("Dashboard refreshed.")).catch(e=>message(e.message,true));

q("saveConfig").onclick=async()=>{
  try{
    await api("/api/admin-config-update",{method:"POST",body:JSON.stringify({
      latestVersion:q("latestVersion").value.trim(),
      minimumVersion:q("minimumVersion").value.trim(),
      premiumEnabled:q("premiumEnabled").checked,
      googleDriveEnabled:q("googleDriveEnabled").checked,
      oneDriveEnabled:q("oneDriveEnabled").checked,
      maintenanceMessage:q("maintenanceMessage").value.trim()
    })});
    message("Configuration saved.");
  }catch(e){message(e.message,true)}
};

auth.onAuthStateChanged(async user=>{
  q("message").classList.add("hidden");
  if(!user){
    q("identity").textContent="";
    q("signin").classList.remove("hidden");
    q("signout").classList.add("hidden");
    q("bootstrap").classList.add("hidden");
    q("app").classList.add("hidden");
    return;
  }

  q("identity").textContent=user.email||user.uid;
  q("signin").classList.add("hidden");
  q("signout").classList.remove("hidden");

  try{
    if(!(await isAdmin())){
      q("bootstrap").classList.remove("hidden");
      q("app").classList.add("hidden");
      return;
    }
    q("bootstrap").classList.add("hidden");
    q("app").classList.remove("hidden");
    await loadAll();
  }catch(e){message(e.message,true)}
});