import { getAuth } from "firebase-admin/auth";
export type VerifiedUser = {
  uid: string;
  email: string;
  creationTime: Date;
};

export async function verifyBearer(request: any): Promise<VerifiedUser> {
  const authorization = request.get("authorization") || "";

  if (!authorization.startsWith("Bearer ")) {
    throw new Error("UNAUTHENTICATED");
  }

  const idToken = authorization.substring("Bearer ".length).trim();

  const decoded = await getAuth().verifyIdToken(idToken, true);
  const user = await getAuth().getUser(decoded.uid);

  const creationTime = new Date(user.metadata.creationTime);

  if (Number.isNaN(creationTime.getTime())) {
    throw new Error("ACCOUNT_CREATION_TIME_UNAVAILABLE");
  }

  return {
    uid: user.uid,
    email: user.email ?? "",
    creationTime,
  };
}
